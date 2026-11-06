using System.Diagnostics;
using System.Text;
using DbViz.Core;
using DbViz.Core.Model;
using Oracle.ManagedDataAccess.Client;
using Q = DbViz.Oracle.DictionaryQueries;

namespace DbViz.Oracle;

/// <summary>Reads the Oracle data dictionary for one schema and builds a <see cref="Snapshot"/>.</summary>
public sealed class OracleExtractor(OracleConnectionSettings settings, ILog log)
{
    public Snapshot Extract(string schema)
    {
        schema = schema.Trim().ToUpperInvariant();
        var total = Stopwatch.StartNew();
        var s = new Snapshot
        {
            Schema = schema,
            ExtractedAt = DateTimeOffset.Now,
            ToolVersion = typeof(OracleExtractor).Assembly.GetName().Version?.ToString(),
        };

        log.Info($"Connecting to {OracleConnectionSettings.NormalizeDataSource(settings.DataSource)} as {settings.User}…");
        using var db = new QueryRunner(settings, log);
        log.Info($"Connected. Read-only transaction: {(db.ReadOnlyTransaction ? "yes" : "no")}. Schema: {schema}");

        // Each query is isolated: missing privilege / missing view → warning, extraction continues.
        List<T> Try<T>(Q.Query q, Func<Row, T> map)
        {
            try { return db.Run(q, schema, map); }
            catch (OracleException ex)
            {
                var msg = $"ORA-{ex.Number:D5}: {ex.Message.Trim()}";
                log.Warn($"  {q.Name,-20} ERROR {msg}");
                s.Warnings.Add(new ExtractWarning(q.Name, msg));
                return [];
            }
        }

        s.DbVersion = Try(Q.Version, r => r.Str("v")).FirstOrDefault();

        s.Objects = Try(Q.Objects, r => new DbObjectRow(r.ReqStr("object_name"), r.ReqStr("object_type"), r.ReqStr("status"),
            r.Date("created"), r.Date("last_ddl_time")));

        s.Tables = Try(Q.Tables, r => new TableRow(r.ReqStr("table_name"), r.Long("num_rows"), r.Date("last_analyzed"),
            r.Flag("partitioned"), r.Flag("temporary"), r.Str("iot_type")));

        s.Views = Try(Q.Views, r => new ViewRow(r.ReqStr("view_name"), r.Str("text")));

        s.MViews = Try(Q.MViews, r => new MViewRow(r.ReqStr("mview_name"), r.Str("query"), r.Str("refresh_mode"),
            r.Str("refresh_method"), r.Date("last_refresh_date"), r.Str("staleness")));

        s.Columns = Try(Q.Columns, r => new ColumnRow(
            r.ReqStr("table_name"), r.Int("column_id") ?? 0, r.ReqStr("column_name"), r.Str("data_type"), r.Str("data_type_owner"),
            r.Int("data_length"), r.Int("data_precision"), r.Int("data_scale"), r.Int("char_length"), r.Str("char_used"),
            r.Flag("nullable"), r.Str("data_default")?.Trim(), r.Flag("virtual_column"), r.Flag("identity_column")));

        s.TableComments = Try(Q.TableComments, r => new TableCommentRow(r.ReqStr("table_name"), r.ReqStr("table_type"), r.ReqStr("comments")));
        s.ColumnComments = Try(Q.ColumnComments, r => new ColumnCommentRow(r.ReqStr("table_name"), r.ReqStr("column_name"), r.ReqStr("comments")));

        s.Constraints = Try(Q.Constraints, r => new ConstraintRow(
            r.ReqStr("constraint_name"), r.ReqStr("constraint_type"), r.ReqStr("table_name"), r.ReqStr("status"),
            r.Str("r_owner"), r.Str("r_constraint_name"), r.Str("r_table_name"), r.Str("delete_rule"),
            r.Str("search_condition")?.Trim(), r.Str("generated") == "GENERATED NAME"));

        s.ConstraintColumns = Try(Q.ConstraintColumns, r => new ConstraintColumnRow(r.ReqStr("owner"), r.ReqStr("constraint_name"),
            r.ReqStr("table_name"), r.ReqStr("column_name"), r.Int("position")));

        s.Indexes = Try(Q.Indexes, r => new IndexRow(r.ReqStr("index_name"), r.ReqStr("table_owner"), r.ReqStr("table_name"),
            r.ReqStr("index_type"), r.Str("uniqueness") == "UNIQUE", r.ReqStr("status"), r.Flag("partitioned")));

        var expressions = Try(Q.IndexExpressions, r => (Index: r.ReqStr("index_name"), Pos: r.Int("column_position") ?? 0,
                Expr: r.Str("column_expression")?.Trim()))
            .ToDictionary(e => (e.Index, e.Pos), e => e.Expr);
        s.IndexColumns = Try(Q.IndexColumns, r =>
        {
            var idx = r.ReqStr("index_name");
            var pos = r.Int("column_position") ?? 0;
            return new IndexColumnRow(idx, r.ReqStr("column_name"), pos, r.Str("descend") == "DESC", expressions.GetValueOrDefault((idx, pos)));
        });

        s.Subprograms = Try(Q.Subprograms, r => new SubprogramRow(r.ReqStr("object_name"), r.Str("procedure_name"), r.ReqStr("object_type"),
            r.Str("overload"), r.Int("subprogram_id") ?? 0, r.Flag("pipelined"), r.Flag("deterministic"), r.Str("authid")));

        s.Arguments = Try(Q.Arguments, r => new ArgumentRow(
            r.Str("package_name"), r.ReqStr("object_name"), r.Str("overload"), r.Int("subprogram_id") ?? 0,
            r.Int("position") ?? 0, r.Int("sequence") ?? 0, r.Str("argument_name"), r.Str("in_out"),
            r.Str("data_type"), r.Str("type_owner"), r.Str("type_name"), r.Str("type_subname"), r.Str("pls_type"),
            r.Int("data_length"), r.Int("data_precision"), r.Int("data_scale"), r.Flag("defaulted")));

        s.Sources = AggregateSource(Try(Q.Source, r => (Name: r.ReqStr("name"), Type: r.ReqStr("type"), Text: r.Str("text") ?? "")));

        s.Dependencies = Try(Q.Dependencies, r => new DependencyRow(r.ReqStr("name"), r.ReqStr("type"), r.ReqStr("referenced_owner"),
            r.ReqStr("referenced_name"), r.ReqStr("referenced_type"), r.Str("referenced_link_name"), r.ReqStr("dependency_type")));

        s.Triggers = Try(Q.Triggers, r => new TriggerRow(r.ReqStr("trigger_name"), r.ReqStr("trigger_type"), r.ReqStr("triggering_event"),
            r.Str("table_owner")?.Trim(), r.Str("table_name")?.Trim(), r.ReqStr("base_object_type").Trim(), r.ReqStr("status"),
            r.Str("when_clause"), r.Str("description")));

        s.Sequences = Try(Q.Sequences, r => new SequenceRow(r.ReqStr("sequence_name"), r.Str("min_value"), r.Str("max_value"),
            r.Str("increment_by"), r.Flag("cycle_flag"), r.Flag("order_flag"), r.Str("cache_size"), r.Str("last_number")));

        s.Synonyms = Try(Q.Synonyms, r => new SynonymRow(r.ReqStr("synonym_name"), r.Str("table_owner"), r.Str("table_name"), r.Str("db_link")));

        s.Types = Try(Q.Types, r => new TypeRow(r.ReqStr("type_name"), r.Str("typecode"), r.Int("attributes"), r.Int("methods"), r.Str("supertype_name")));
        s.TypeAttributes = Try(Q.TypeAttributes, r => new TypeAttributeRow(r.ReqStr("type_name"), r.Int("attr_no") ?? 0, r.ReqStr("attr_name"),
            r.Str("attr_type_owner"), r.Str("attr_type_name"), r.Int("length"), r.Int("precision"), r.Int("scale")));
        s.CollectionTypes = Try(Q.CollectionTypes, r => new CollectionTypeRow(r.ReqStr("type_name"), r.ReqStr("coll_type"),
            r.Str("elem_type_owner"), r.Str("elem_type_name"), r.Str("upper_bound")));

        s.DbLinks = Try(Q.DbLinks, r => new DbLinkRow(r.ReqStr("db_link"), r.Str("host"), r.Date("created")));

        s.Jobs = Try(Q.Jobs, r => new JobRow(r.ReqStr("job_name"), r.Str("job_type"), r.Str("job_action"), r.Str("program_name"),
            r.Str("repeat_interval"), r.Flag("enabled"), r.Str("state"), r.Str("comments"), r.Str("last_start_date")));

        s.Errors = Try(Q.Errors, r => new CompileErrorRow(r.ReqStr("name"), r.ReqStr("type"), r.Int("line") ?? 0, r.Int("position") ?? 0,
            r.ReqStr("text").Trim(), r.ReqStr("attribute")));

        log.Info($"Extraction finished in {total.Elapsed.TotalSeconds:F1} s, warnings: {s.Warnings.Count}");
        return s;
    }

    /// <summary>Connection check: who am I, DB version, object counts by type in the schema.</summary>
    public void Check(string schema)
    {
        schema = schema.Trim().ToUpperInvariant();
        using var db = new QueryRunner(settings, log);
        var who = db.Run(Q.CurrentUser, schema, r => (User: r.Str("username"), Db: r.Str("db_name"))).First();
        log.Info($"User: {who.User}, database: {who.Db}, read-only transaction: {(db.ReadOnlyTransaction ? "yes" : "no")}");
        try { log.Info($"Version: {db.Run(Q.Version, schema, r => r.Str("v")).FirstOrDefault()}"); }
        catch (OracleException ex) { log.Warn($"Could not get the version: ORA-{ex.Number:D5}"); }
        var counts = db.Run(Q.ObjectCounts, schema, r => (Type: r.ReqStr("object_type"), Status: r.ReqStr("status"), Count: r.Long("cnt") ?? 0));
        log.Info($"Objects in schema {schema}:");
        foreach (var c in counts) log.Info($"  {c.Type,-22} {c.Status,-8} {c.Count,8}");
        if (counts.Count == 0) log.Warn($"ALL_OBJECTS has no objects owned by {schema}; check the schema name and privileges.");
    }

    private static List<SourceRow> AggregateSource(List<(string Name, string Type, string Text)> lines)
    {
        var result = new List<SourceRow>();
        var sb = new StringBuilder();
        string? name = null, type = null;
        var count = 0;
        foreach (var l in lines)
        {
            if (l.Name != name || l.Type != type)
            {
                if (name is not null) result.Add(new SourceRow(name, type!, sb.ToString().TrimEnd(), count));
                sb.Clear(); count = 0; name = l.Name; type = l.Type;
            }
            sb.Append(l.Text.EndsWith('\n') ? l.Text : l.Text + "\n");
            count++;
        }
        if (name is not null) result.Add(new SourceRow(name, type!, sb.ToString().TrimEnd(), count));
        return result;
    }
}
