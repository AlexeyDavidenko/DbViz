using System.Globalization;
using System.Text;
using DbViz.Core.Model;

namespace DbViz.Obsidian;

public sealed class RenderOptions
{
    /// <summary>Maximum number of source lines in a note (the rest is truncated).</summary>
    public int MaxSourceLines { get; init; } = 3000;
    /// <summary>Maximum number of nodes on each side of the relationship diagram.</summary>
    public int MaxDiagramNodes { get; init; } = 25;
}

/// <summary>Builds a <see cref="Note"/> for every schema object.</summary>
public sealed class NoteRenderer(SchemaIndex ix, VaultLayout layout, RenderOptions opt)
{
    public Note Render(DbObject o) => o.Kind switch
    {
        ObjectKind.Table => Table(o),
        ObjectKind.View => View(o),
        ObjectKind.MaterializedView => MView(o),
        ObjectKind.Package => Package(o),
        ObjectKind.Procedure or ObjectKind.Function => Standalone(o),
        ObjectKind.Trigger => Trigger(o),
        ObjectKind.Sequence => Sequence(o),
        ObjectKind.Synonym => Synonym(o),
        ObjectKind.Type => TypeNote(o),
        ObjectKind.DbLink => DbLink(o),
        ObjectKind.Job => Job(o),
        _ => Base(o),
    };

    // ======================================================================
    // Descriptions
    // ======================================================================

    public (string? Text, string? Source) Describe(DbObject o)
    {
        string? text = o.Kind switch
        {
            ObjectKind.Table or ObjectKind.View or ObjectKind.MaterializedView => ix.CommentOf(o.Name),
            _ => null,
        };
        if (text is not null) return (text, "db-comment");

        text = o.Kind switch
        {
            ObjectKind.Package => SourceComments.HeaderComment(ix.Source(o.Name, "PACKAGE")?.Text),
            ObjectKind.Procedure => SourceComments.HeaderComment(ix.Source(o.Name, "PROCEDURE")?.Text),
            ObjectKind.Function => SourceComments.HeaderComment(ix.Source(o.Name, "FUNCTION")?.Text),
            ObjectKind.Trigger => SourceComments.HeaderComment(ix.Source(o.Name, "TRIGGER")?.Text),
            ObjectKind.Type => SourceComments.HeaderComment(ix.Source(o.Name, "TYPE")?.Text),
            ObjectKind.Job => ix.Snapshot.Jobs.FirstOrDefault(j => j.Name == o.Name)?.Comments,
            _ => null,
        };
        if (!string.IsNullOrWhiteSpace(text)) return (text.Trim(), o.Kind == ObjectKind.Job ? "db-comment" : "source-header");
        return (null, null);
    }

    // ======================================================================
    // Common part
    // ======================================================================

    private Note Base(DbObject o)
    {
        var (desc, descSource) = Describe(o);
        var tags = new List<string> { $"db/{KindInfo.TypeCode(o.Kind)}", KindInfo.LayerTag(o.Layer), $"schema/{ix.Schema}" };
        if (!o.IsValid) tags.Add("status/invalid");

        var note = new Note { Id = o.Id, RelativePath = layout.RelativePath(o), Title = o.Name };
        note.Fm("dbviz_id", o.Id)
            .Fm("db_type", KindInfo.TypeCode(o.Kind))
            .Fm("layer", KindInfo.LayerCode(o.Layer))
            .Fm("schema", ix.Schema)
            .Fm("name", o.Name)
            .Fm("status", o.Status)
            .Fm("created", o.Created)
            .Fm("last_ddl_time", o.LastDdlTime)
            .Fm("description_source", descSource ?? "none")
            .Fm("tags", tags);

        var sb = new StringBuilder();
        var callout = o.IsValid ? "info" : "danger";
        // no link to the schema MOC: linking the MOC from every note turns it into a hub that collapses the whole graph
        sb.Append($"> [!{callout}] {KindInfo.Title(o.Kind)} · {KindInfo.LayerTitle(o.Layer)} · schema {ix.Schema}");
        if (!o.IsValid) sb.Append($" · **{o.Status}**");
        sb.Append("\n\n");
        sb.Append(desc ?? "_No description. Add one in the \"Notes\" section or via COMMENT ON in the database._");
        note.Block("description", sb.ToString());
        return note;
    }

    private static string Props(params (string Key, string? Value)[] items) =>
        Md.Bullets(items.Where(i => !string.IsNullOrWhiteSpace(i.Value)).Select(i => $"**{i.Key}:** {i.Value}"));

    private static string? Dt(DateTime? d) => d?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    private string CommonProps(DbObject o, params (string, string?)[] extra) =>
        Props([.. extra, ("Created", Dt(o.Created)), ("Last DDL", Dt(o.LastDdlTime))]);

    private string Errors(DbObject o)
    {
        var errs = ix.ErrorsByName[o.Name].ToList();
        if (errs.Count == 0) return "";
        return "## Compilation errors\n\n" + Md.Table(["Part", "Line", "Position", "Kind", "Text"],
            errs.Select(e => new[] { e.Type, e.Line.ToString(), e.Position.ToString(), e.Attribute, e.Text }));
    }

    private string LinkOrExternal(Edge e) =>
        e.ToId is not null && ix.Get(e.ToId) is { } t ? layout.Link(t) : Md.Code(e.ToExternal!.Display) + " _(external)_";

    private string LinkFrom(Edge e) => layout.Link(ix.Get(e.FromId)!);

    /// <summary>Relations section: outgoing and incoming, grouped by edge kind and object kind.</summary>
    private string Relations(DbObject o)
    {
        var sb = new StringBuilder("## Relationships\n");
        var outgoing = ix.Outgoing(o.Id);
        var incoming = ix.Incoming(o.Id);

        void Group(string title, IEnumerable<Edge> edges, bool isIncoming)
        {
            var list = edges.ToList();
            if (list.Count == 0) return;
            sb.Append($"\n### {title}\n");
            var groups = list
                .Select(e => (Edge: e, Target: isIncoming ? ix.Get(e.FromId) : (e.ToId is null ? null : ix.Get(e.ToId))))
                .GroupBy(x => x.Target is null ? "External objects" : KindInfo.Plural(x.Target.Kind))
                .OrderBy(g => g.Key == "External objects" ? 1 : 0).ThenBy(g => g.Key);
            foreach (var g in groups)
            {
                var links = g.Select(x =>
                    {
                        var link = isIncoming ? LinkFrom(x.Edge) : LinkOrExternal(x.Edge);
                        return x.Edge.Detail is null || x.Edge.Kind == EdgeKind.Fk ? link : $"{link} ({x.Edge.Detail})";
                    })
                    .Distinct().OrderBy(s => s, StringComparer.Ordinal);
                sb.Append($"- **{g.Key}:** {string.Join(", ", links)}\n");
            }
        }

        Group("References (FK)", outgoing.Where(e => e.Kind == EdgeKind.Fk), false);
        Group("Referenced by (FK)", incoming.Where(e => e.Kind == EdgeKind.Fk), true);
        Group("Depends on", outgoing.Where(e => e.Kind == EdgeKind.Depends), false);
        Group("Used by", incoming.Where(e => e.Kind == EdgeKind.Depends), true);
        Group("Trigger on", outgoing.Where(e => e.Kind == EdgeKind.TriggerOn), false);
        Group("Triggers", incoming.Where(e => e.Kind == EdgeKind.TriggerOn), true);
        Group("Points to", outgoing.Where(e => e.Kind == EdgeKind.SynonymFor), false);
        Group("Synonyms", incoming.Where(e => e.Kind == EdgeKind.SynonymFor), true);
        Group("Calls", outgoing.Where(e => e.Kind == EdgeKind.JobCalls), false);
        Group("Run by jobs", incoming.Where(e => e.Kind == EdgeKind.JobCalls), true);

        return outgoing.Count + incoming.Count == 0 ? "## Relationships\n\n_No relationships found._" : sb.ToString().TrimEnd();
    }

    /// <summary>Local diagram "who uses → object → what it uses", colored by layer, nodes are clickable.</summary>
    private string FlowDiagram(DbObject o)
    {
        var incoming = ix.Incoming(o.Id).Where(e => e.Kind != EdgeKind.Fk).Select(e => e.FromId).Distinct().ToList();
        var outgoing = ix.Outgoing(o.Id).Where(e => e.Kind != EdgeKind.Fk).ToList();
        if (incoming.Count == 0 && outgoing.Count == 0) return "";

        var sb = new StringBuilder("## Relationship diagram\n\n```mermaid\nflowchart LR\n");
        sb.Append("  classDef storage fill:#dbeafe,stroke:#2563eb,color:#111\n");
        sb.Append("  classDef access fill:#dcfce7,stroke:#16a34a,color:#111\n");
        sb.Append("  classDef logic fill:#ffedd5,stroke:#ea580c,color:#111\n");
        sb.Append("  classDef integration fill:#f3e8ff,stroke:#9333ea,color:#111\n");
        sb.Append("  classDef ext fill:#f4f4f5,stroke:#71717a,color:#111,stroke-dasharray:4 3\n");

        var internalNodes = new List<string>();
        var declared = new HashSet<string>();
        string Node(DbObject x)
        {
            var id = Md.MermaidId("n_" + layout.FileName(x));
            if (declared.Add(id))
            {
                sb.Append($"  {id}[\"{layout.FileName(x)}\"]\n");
                sb.Append($"  class {id} {x.Layer.ToString().ToLowerInvariant()}\n");
                internalNodes.Add(id);
            }
            return id;
        }

        var self = Node(o);
        sb.Append($"  style {self} stroke-width:3px\n");

        foreach (var fromId in incoming.Take(opt.MaxDiagramNodes))
        {
            var from = Node(ix.Get(fromId)!); // the node declaration is written to sb before the edge line
            sb.Append($"  {from} --> {self}\n");
        }
        if (incoming.Count > opt.MaxDiagramNodes)
            sb.Append($"  more_in([\"… {incoming.Count - opt.MaxDiagramNodes} more\"]) -.-> {self}\n");

        var shown = 0;
        foreach (var e in outgoing)
        {
            if (shown++ >= opt.MaxDiagramNodes) break;
            var label = e.Kind switch { EdgeKind.TriggerOn => "|trigger|", EdgeKind.SynonymFor => "|synonym|", EdgeKind.JobCalls => "|job|", _ => "" };
            if (e.ToId is not null)
            {
                var to = Node(ix.Get(e.ToId)!);
                sb.Append($"  {self} -->{label} {to}\n");
            }
            else
            {
                var id = Md.MermaidId("x_" + e.ToExternal!.Display);
                if (declared.Add(id)) sb.Append($"  {id}[\"{e.ToExternal.Display}\"]\n  class {id} ext\n");
                sb.Append($"  {self} -.->{label} {id}\n");
            }
        }
        if (outgoing.Count > opt.MaxDiagramNodes)
            sb.Append($"  {self} -.-> more_out([\"… {outgoing.Count - opt.MaxDiagramNodes} more\"])\n");

        // internal-link makes nodes clickable in Obsidian (node text = note name)
        sb.Append($"  class {string.Join(",", internalNodes)} internal-link\n");
        sb.Append("```");
        return sb.ToString();
    }

    private string SourceBlock(string name, params (string Type, string Title)[] parts)
    {
        var sb = new StringBuilder();
        foreach (var (type, title) in parts)
        {
            var src = ix.Source(name, type);
            if (src is null) continue;
            sb.Append(Md.CodeCallout($"{title} ({src.Lines} lines)", "sql", src.Text, opt.MaxSourceLines)).Append("\n\n");
        }
        return sb.Length == 0 ? "" : "## Source code\n\n" + sb.ToString().TrimEnd();
    }

    // ======================================================================
    // Tables, views
    // ======================================================================

    private Note Table(DbObject o)
    {
        var note = Base(o);
        var t = ix.Snapshot.Tables.FirstOrDefault(x => x.Name == o.Name);
        if (t?.NumRows is not null) note.Fm("num_rows", t.NumRows);
        if (t?.LastAnalyzed is not null) note.Fm("last_analyzed", t.LastAnalyzed);

        note.Block("properties", "## Properties\n\n" + CommonProps(o,
            ("Rows (statistics)", t?.NumRows?.ToString("N0", CultureInfo.InvariantCulture)),
            ("Statistics gathered", Dt(t?.LastAnalyzed)),
            ("Partitioned", t?.Partitioned == true ? "yes" : null),
            ("Temporary", t?.Temporary == true ? "yes" : null),
            ("IOT", t?.IotType)));

        note.Block("columns", ColumnsTable(o, withKeys: true));
        note.Block("constraints", Constraints(o));
        note.Block("indexes", Indexes(o));
        note.Block("relations", Relations(o));
        note.Block("diagram", string.Join("\n\n", new[] { ErDiagram(o), FlowDiagram(o) }.Where(s => s.Length > 0)));
        return note;
    }

    private Note View(DbObject o)
    {
        var note = Base(o);
        note.Block("properties", "## Properties\n\n" + CommonProps(o));
        note.Block("columns", ColumnsTable(o, withKeys: false));
        note.Block("relations", Relations(o));
        note.Block("diagram", FlowDiagram(o));
        var text = ix.Snapshot.Views.FirstOrDefault(v => v.Name == o.Name)?.Text;
        note.Block("source", string.IsNullOrWhiteSpace(text) ? "" : "## SQL\n\n" + Md.CodeCallout("View text", "sql", text, opt.MaxSourceLines));
        note.Block("errors", Errors(o));
        return note;
    }

    private Note MView(DbObject o)
    {
        var note = Base(o);
        var m = ix.Snapshot.MViews.FirstOrDefault(x => x.Name == o.Name);
        note.Block("properties", "## Properties\n\n" + CommonProps(o,
            ("Refresh", m is null ? null : $"{m.RefreshMethod} / {m.RefreshMode}"),
            ("Last refresh", Dt(m?.LastRefresh)),
            ("Staleness", m?.Staleness)));
        note.Block("columns", ColumnsTable(o, withKeys: true));
        note.Block("indexes", Indexes(o));
        note.Block("relations", Relations(o));
        note.Block("diagram", FlowDiagram(o));
        note.Block("source", string.IsNullOrWhiteSpace(m?.Query) ? "" : "## SQL\n\n" + Md.CodeCallout("Query", "sql", m!.Query, opt.MaxSourceLines));
        note.Block("errors", Errors(o));
        return note;
    }

    private string ColumnsTable(DbObject o, bool withKeys)
    {
        var cols = ix.ColumnsByTable[o.Name].ToList();
        if (cols.Count == 0) return "## Columns\n\n_No column data._";

        var keys = withKeys ? ColumnKeys(o.Name) : [];
        var rows = cols.Select(c =>
        {
            var cells = new List<string?>
            {
                c.ColumnId.ToString(),
                "**" + c.Name + "**",
                TypeWithLink(c) + (c.Virtual ? " _(virtual)_" : "") + (c.Identity ? " _(identity)_" : ""),
                c.Nullable ? "" : "NOT NULL",
                Md.Code(Md.Truncate(c.DataDefault, 60)),
            };
            if (withKeys) cells.Add(keys.TryGetValue(c.Name, out var k) ? string.Join("<br>", k) : "");
            cells.Add(ix.ColumnComment(o.Name, c.Name));
            return (IReadOnlyList<string?>)cells;
        });

        string[] headers = withKeys
            ? ["#", "Column", "Type", "Null", "Default", "Key", "Description"]
            : ["#", "Column", "Type", "Null", "Default", "Description"];
        return $"## Columns ({cols.Count})\n\n" + Md.Table(headers, rows);
    }

    private string TypeWithLink(ColumnRow c)
    {
        var text = OracleTypes.Format(c);
        if (c.DataTypeOwner == ix.Schema && c.DataType is not null && ix.FindMain(c.DataType) is { } type)
            return layout.Link(type);
        return text;
    }

    private Dictionary<string, List<string>> ColumnKeys(string table)
    {
        var result = new Dictionary<string, List<string>>();
        void Add(string col, string mark) { (result.TryGetValue(col, out var l) ? l : result[col] = []).Add(mark); }

        foreach (var c in ix.ConstraintsByTable[table])
        {
            var cols = ix.ConstraintColumns[(ix.Schema, c.Name)].ToList();
            switch (c.Type)
            {
                case "P": foreach (var cc in cols) Add(cc.ColumnName, "PK"); break;
                case "U": foreach (var cc in cols) Add(cc.ColumnName, "UK"); break;
                case "R":
                    var refCols = ix.ConstraintColumns[(c.RefOwner ?? ix.Schema, c.RefConstraintName ?? "")].ToList();
                    var target = c.RefOwner == ix.Schema && c.RefTableName is not null ? ix.FindMain(c.RefTableName) : null;
                    var targetText = target is not null ? layout.Link(target) : Md.Code($"{c.RefOwner}.{c.RefTableName}");
                    for (var i = 0; i < cols.Count; i++)
                    {
                        var refCol = i < refCols.Count ? refCols[i].ColumnName : "?";
                        Add(cols[i].ColumnName, $"FK → {targetText}.{refCol}");
                    }
                    break;
            }
        }
        return result;
    }

    /// <summary>NOT NULL checks that Oracle creates for columns automatically are hidden.</summary>
    public static bool IsNotNullCheck(ConstraintRow c) =>
        c.Type == "C" && c.SearchCondition is not null &&
        System.Text.RegularExpressions.Regex.IsMatch(c.SearchCondition, @"^\s*""?[\w$#]+""?\s+IS\s+NOT\s+NULL\s*$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private string Constraints(DbObject o)
    {
        var all = ix.ConstraintsByTable[o.Name].Where(c => !IsNotNullCheck(c)).OrderBy(c => c.Type).ThenBy(c => c.Name).ToList();
        if (all.Count == 0) return "";
        string Cols(string owner, string name) => string.Join(", ", ix.ConstraintColumns[(owner, name)].Select(c => c.ColumnName));

        var sb = new StringBuilder("## Constraints\n\n");
        var pk = all.FirstOrDefault(c => c.Type == "P");
        sb.Append(pk is null ? "**Primary key:** _none_\n\n" : $"**Primary key:** {Md.Code(pk.Name)} ({Cols(ix.Schema, pk.Name)}){(pk.Status != "ENABLED" ? " — " + pk.Status : "")}\n\n");

        var uks = all.Where(c => c.Type == "U").ToList();
        if (uks.Count > 0)
            sb.Append("**Unique:**\n\n").Append(Md.Table(["Name", "Columns", "Status"],
                uks.Select(c => new[] { c.Name, Cols(ix.Schema, c.Name), c.Status }))).Append("\n\n");

        var fks = all.Where(c => c.Type == "R").ToList();
        if (fks.Count > 0)
            sb.Append("**Foreign keys:**\n\n").Append(Md.Table(["Name", "Columns", "→ Table", "→ Columns", "ON DELETE", "Status"],
                fks.Select(c =>
                {
                    var target = c.RefOwner == ix.Schema && c.RefTableName is not null ? ix.FindMain(c.RefTableName) : null;
                    return new[]
                    {
                        c.Name, Cols(ix.Schema, c.Name),
                        target is not null ? layout.Link(target) : Md.Code($"{c.RefOwner}.{c.RefTableName}"),
                        Cols(c.RefOwner ?? ix.Schema, c.RefConstraintName ?? ""), c.DeleteRule, c.Status,
                    };
                }))).Append("\n\n");

        var checks = all.Where(c => c.Type == "C").ToList();
        if (checks.Count > 0)
            sb.Append("**Check:**\n\n").Append(Md.Table(["Name", "Condition", "Status"],
                checks.Select(c => new[] { c.Name, Md.Code(c.SearchCondition), c.Status })));
        return sb.ToString().TrimEnd();
    }

    private string Indexes(DbObject o)
    {
        var list = ix.IndexesByTable[o.Name].OrderBy(i => i.Name).ToList();
        if (list.Count == 0) return "";
        return "## Indexes\n\n" + Md.Table(["Name", "Type", "Unique", "Columns", "Status"],
            list.Select(i => new[]
            {
                i.Name, i.IndexType + (i.Partitioned ? ", partitioned" : ""), i.Unique ? "yes" : "",
                string.Join(", ", ix.IndexColumns[i.Name].Select(c => (c.Expression ?? c.ColumnName) + (c.Descending ? " DESC" : ""))),
                i.Status,
            }));
    }

    private string ErDiagram(DbObject o)
    {
        var fkOut = ix.Outgoing(o.Id).Where(e => e.Kind == EdgeKind.Fk).ToList();
        var fkIn = ix.Incoming(o.Id).Where(e => e.Kind == EdgeKind.Fk).ToList();
        if (fkOut.Count + fkIn.Count == 0) return "";

        string Ent(Edge e, bool target) => target
            ? (e.ToId is not null ? Md.MermaidId(ix.Get(e.ToId)!.Name) : Md.MermaidId(e.ToExternal!.Display))
            : Md.MermaidId(ix.Get(e.FromId)!.Name);

        var sb = new StringBuilder("## ER diagram (FK, 1 hop)\n\n```mermaid\nerDiagram\n");
        foreach (var e in fkOut.Take(opt.MaxDiagramNodes))
            sb.Append($"  {Md.MermaidId(o.Name)} }}o--|| {Ent(e, true)} : \"{e.Detail}\"\n");
        foreach (var e in fkIn.Take(opt.MaxDiagramNodes))
            sb.Append($"  {Ent(e, false)} }}o--|| {Md.MermaidId(o.Name)} : \"{e.Detail}\"\n");
        sb.Append("```");
        return sb.ToString();
    }

    // ======================================================================
    // Code: packages, procedures, functions, triggers, types
    // ======================================================================

    private Note Package(DbObject o)
    {
        var note = Base(o);
        var members = ix.SubprogramsByObject[o.Name].Where(s => s.ProcedureName is not null).ToList();
        var pkgRow = ix.SubprogramsByObject[o.Name].FirstOrDefault(s => s.ProcedureName is null);
        note.Fm("subprograms", members.Count);

        note.Block("properties", "## Properties\n\n" + CommonProps(o,
            ("Executes with rights of", pkgRow?.AuthId),
            ("Public subprograms", members.Count.ToString()),
            ("Package body", ix.Source(o.Name, "PACKAGE BODY") is null ? "_not found / no access_" : null)));

        var spec = ix.Source(o.Name, "PACKAGE")?.Text;
        var sb = new StringBuilder($"## Subprograms ({members.Count})\n");
        var seen = new Dictionary<string, int>();
        foreach (var m in members)
        {
            var name = m.ProcedureName!;
            var n = seen[name] = seen.GetValueOrDefault(name) + 1;
            var args = ix.ArgumentsOf(o.Name, name, m.Overload).ToList();
            sb.Append($"\n### {name}{(n > 1 ? $" (overload {n})" : "")}\n\n");
            if (n == 1 && SourceComments.CommentBefore(spec, name) is { } c) sb.Append("> ").Append(c).Append("\n\n");
            sb.Append(Signature(name, args, m)).Append('\n');
            var p = Parameters(args);
            if (p.Length > 0) sb.Append('\n').Append(p).Append('\n');
        }
        note.Block("subprograms", members.Count == 0 ? "" : sb.ToString().TrimEnd());
        note.Block("relations", Relations(o));
        note.Block("diagram", FlowDiagram(o));
        note.Block("source", SourceBlock(o.Name, ("PACKAGE", "Specification"), ("PACKAGE BODY", "Body")));
        note.Block("errors", Errors(o));
        return note;
    }

    private Note Standalone(DbObject o)
    {
        var note = Base(o);
        var row = ix.SubprogramsByObject[o.Name].FirstOrDefault();
        var args = ix.ArgumentsOf(null, o.Name, null).ToList();
        note.Block("properties", "## Properties\n\n" + CommonProps(o, ("Executes with rights of", row?.AuthId)));
        note.Block("signature", "## Signature\n\n" + Signature(o.Name, args, row, o.Kind == ObjectKind.Function) +
                                 (Parameters(args) is { Length: > 0 } p ? "\n\n" + p : ""));
        note.Block("relations", Relations(o));
        note.Block("diagram", FlowDiagram(o));
        var type = o.Kind == ObjectKind.Function ? "FUNCTION" : "PROCEDURE";
        note.Block("source", SourceBlock(o.Name, (type, "Source")));
        note.Block("errors", Errors(o));
        return note;
    }

    private static string Signature(string name, List<ArgumentRow> args, SubprogramRow? row, bool? isFunction = null)
    {
        var fn = isFunction ?? SchemaIndex.IsFunction(args);
        var ps = args.Where(a => a.Position > 0)
            .Select(a => $"{a.ArgumentName} {(a.InOut ?? "IN").Replace("/", " ")} {OracleTypes.Format(a)}{(a.Defaulted ? " := …" : "")}");
        var ret = args.FirstOrDefault(a => a.Position == 0);
        var sb = new StringBuilder("```sql\n");
        sb.Append(fn ? "FUNCTION " : "PROCEDURE ").Append(name);
        var list = ps.ToList();
        if (list.Count > 0) sb.Append("(\n    ").Append(string.Join(",\n    ", list)).Append("\n)");
        if (ret is not null) sb.Append(" RETURN ").Append(OracleTypes.Format(ret));
        if (row?.Pipelined == true) sb.Append(" PIPELINED");
        if (row?.Deterministic == true) sb.Append(" DETERMINISTIC");
        sb.Append("\n```");
        return sb.ToString();
    }

    private string Parameters(List<ArgumentRow> args)
    {
        var rows = args.OrderBy(a => a.Position == 0 ? int.MaxValue : a.Position).Select(a => new[]
        {
            a.Position == 0 ? "" : a.Position.ToString(),
            a.Position == 0 ? "**RETURN**" : "**" + a.ArgumentName + "**",
            a.Position == 0 ? "OUT" : a.InOut ?? "IN",
            TypeWithLink(a),
            a.Defaulted ? "yes" : "",
        }).ToList();
        return rows.Count == 0 ? "" : Md.Table(["#", "Parameter", "Direction", "Type", "Default"], rows);
    }

    private string TypeWithLink(ArgumentRow a)
    {
        var text = OracleTypes.Format(a);
        if (a.TypeOwner == ix.Schema && a.TypeName is not null && ix.FindMain(a.TypeName) is { } t)
            return a.TypeSubname is null ? layout.Link(t) : $"{layout.Link(t)}.{a.TypeSubname}";
        return Md.Code(text);
    }

    private Note Trigger(DbObject o)
    {
        var note = Base(o);
        var t = ix.Snapshot.Triggers.FirstOrDefault(x => x.Name == o.Name);
        var on = t?.TableName is null ? null
            : t.TableOwner == ix.Schema && ix.FindMain(t.TableName) is { } target ? layout.Link(target) : Md.Code($"{t.TableOwner}.{t.TableName}");
        note.Block("properties", "## Properties\n\n" + CommonProps(o,
            ("Object", on is null ? t?.BaseObjectType : $"{on} ({t!.BaseObjectType})"),
            ("Type", t?.TriggerType),
            ("Event", t?.TriggeringEvent),
            ("State", t?.Status),
            ("WHEN condition", Md.Code(t?.WhenClause))));
        note.Block("relations", Relations(o));
        note.Block("diagram", FlowDiagram(o));
        note.Block("source", SourceBlock(o.Name, ("TRIGGER", "Source")));
        note.Block("errors", Errors(o));
        return note;
    }

    private Note TypeNote(DbObject o)
    {
        var note = Base(o);
        var t = ix.Snapshot.Types.FirstOrDefault(x => x.Name == o.Name);
        var coll = ix.Snapshot.CollectionTypes.FirstOrDefault(x => x.TypeName == o.Name);
        string? elem = null;
        if (coll is not null)
            elem = coll.ElemTypeOwner == ix.Schema && coll.ElemTypeName is not null && ix.FindMain(coll.ElemTypeName) is { } et
                ? layout.Link(et) : coll.ElemTypeName;
        note.Block("properties", "## Properties\n\n" + CommonProps(o,
            ("Kind", t?.TypeCode),
            ("Collection", coll is null ? null : $"{coll.CollType} OF {elem}{(coll.UpperBound is null ? "" : $" ({coll.UpperBound})")}"),
            ("Inherits from", t?.SuperTypeName)));

        var attrs = ix.TypeAttributes[o.Name].ToList();
        note.Block("attributes", attrs.Count == 0 ? "" : "## Attributes\n\n" + Md.Table(["#", "Attribute", "Type"],
            attrs.Select(a => new[]
            {
                a.AttrNo.ToString(), "**" + a.AttrName + "**",
                a.AttrTypeOwner == ix.Schema && a.AttrTypeName is not null && ix.FindMain(a.AttrTypeName) is { } at
                    ? layout.Link(at)
                    : OracleTypes.Format(a.AttrTypeName, null, a.Length, a.Precision, a.Scale, a.Length, null),
            })));
        note.Block("relations", Relations(o));
        note.Block("diagram", FlowDiagram(o));
        note.Block("source", SourceBlock(o.Name, ("TYPE", "Specification"), ("TYPE BODY", "Body")));
        note.Block("errors", Errors(o));
        return note;
    }

    // ======================================================================
    // Other
    // ======================================================================

    private Note Sequence(DbObject o)
    {
        var note = Base(o);
        var s = ix.Snapshot.Sequences.FirstOrDefault(x => x.Name == o.Name);
        note.Block("properties", "## Properties\n\n" + CommonProps(o,
            ("Increment", s?.IncrementBy), ("Min", s?.MinValue), ("Max", s?.MaxValue),
            ("Cache", s?.CacheSize), ("Cycle", s?.Cycle == true ? "yes" : null), ("Order", s?.Order == true ? "yes" : null),
            ("Last number", s?.LastNumber)));
        note.Block("relations", Relations(o));
        note.Block("diagram", FlowDiagram(o));
        return note;
    }

    private Note Synonym(DbObject o)
    {
        var note = Base(o);
        note.Block("properties", "## Properties\n\n" + CommonProps(o));
        note.Block("relations", Relations(o));
        return note;
    }

    private Note DbLink(DbObject o)
    {
        var note = Base(o);
        var l = ix.Snapshot.DbLinks.FirstOrDefault(x => x.Name == o.Name);
        note.Block("properties", "## Properties\n\n" + CommonProps(o, ("Host", Md.Code(l?.Host))));
        note.Block("relations", Relations(o));
        note.Block("diagram", FlowDiagram(o));
        return note;
    }

    private Note Job(DbObject o)
    {
        var note = Base(o);
        var j = ix.Snapshot.Jobs.FirstOrDefault(x => x.Name == o.Name);
        note.Block("properties", "## Properties\n\n" + CommonProps(o,
            ("Type", j?.JobType), ("Program", j?.ProgramName), ("Schedule", Md.Code(j?.RepeatInterval)),
            ("Enabled", j is null ? null : j.Enabled ? "yes" : "no"), ("State", j?.State), ("Last run", j?.LastStartDate)));
        note.Block("relations", Relations(o));
        note.Block("diagram", FlowDiagram(o));
        note.Block("source", string.IsNullOrWhiteSpace(j?.JobAction) ? "" : "## Action\n\n" + Md.CodeCallout("job_action", "sql", j!.JobAction, opt.MaxSourceLines, collapsed: false));
        return note;
    }
}
