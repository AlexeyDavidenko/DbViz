using System.Text.RegularExpressions;

namespace DbViz.Core.Model;

public sealed class DbObject
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required ObjectKind Kind { get; init; }
    public string Status { get; set; } = "VALID";
    public DateTime? Created { get; set; }
    public DateTime? LastDdlTime { get; set; }
    public Layer Layer => KindInfo.LayerOf(Kind);
    public bool IsValid => Status == "VALID";
}

/// <summary>Object outside the schema (another schema or a db link); it has no note of its own.</summary>
public sealed record ExternalRef(string Owner, string Name, string Type, string? DbLink)
{
    public string Display => DbLink is null ? $"{Owner}.{Name}" : $"{Owner}.{Name}@{DbLink}";
}

public sealed record Edge(string FromId, string? ToId, ExternalRef? ToExternal, EdgeKind Kind, string? Detail);

public sealed class IndexOptions
{
    /// <summary>Dependencies on these schemas are hidden (STANDARD, DBMS_OUTPUT and other noise).</summary>
    public HashSet<string> ExcludedRefOwners { get; init; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "SYS", "PUBLIC", "SYSTEM", "XDB", "MDSYS", "CTXSYS", "ORDSYS", "WMSYS", "OUTLN", "DBSNMP", "APPQOSSYS", "LBACSYS", "OLAPSYS",
    };

    /// <summary>Masks of system-generated objects to hide. "%" matches any characters, everything else is literal.</summary>
    public List<string> ExcludeNamePatterns { get; init; } =
    [
        "BIN$%", "ISEQ$$_%", "MLOG$_%", "RUPD$_%", "DR$%", "AQ$%",
        "SYS_C0%", "SYS_IL%", "SYS_LOB%", "SYS_NC%", "SYS_IOT%", "SYS_PLSQL_%", "SYS_EXPORT_%", "SYS_IMPORT_%",
    ];
}

/// <summary>
/// Logical model on top of the raw <see cref="Snapshot"/>: objects with ids, typed edges, fast lookups.
/// </summary>
public sealed class SchemaIndex
{
    private readonly Dictionary<string, DbObject> _byId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DbObject> _mainNamespace = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DbObject> _triggers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<Edge>> _out = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<Edge>> _in = new(StringComparer.Ordinal);
    private readonly List<Regex> _exclude;
    private readonly IndexOptions _options;

    public Snapshot Snapshot { get; }
    public string Schema => Snapshot.Schema;
    public IReadOnlyCollection<DbObject> Objects => _byId.Values;
    public List<Edge> Edges { get; } = [];

    public ILookup<string, ColumnRow> ColumnsByTable { get; }
    public ILookup<string, ConstraintRow> ConstraintsByTable { get; }
    public ILookup<(string Owner, string Constraint), ConstraintColumnRow> ConstraintColumns { get; }
    public ILookup<string, IndexRow> IndexesByTable { get; }
    public ILookup<string, IndexColumnRow> IndexColumns { get; }
    public ILookup<string, SubprogramRow> SubprogramsByObject { get; }
    public ILookup<(string? Package, string Object, string? Overload), ArgumentRow> ArgumentsBySubprogram { get; }
    public ILookup<string, TriggerRow> TriggersByTable { get; }
    public ILookup<string, CompileErrorRow> ErrorsByName { get; }
    public ILookup<string, TypeAttributeRow> TypeAttributes { get; }

    private readonly Dictionary<string, string> _tableComments = new(StringComparer.Ordinal);
    private readonly Dictionary<(string, string), string> _columnComments = new();
    private readonly Dictionary<(string, string), SourceRow> _sources = new();

    public SchemaIndex(Snapshot snapshot, IndexOptions? options = null)
    {
        Snapshot = snapshot;
        _options = options ?? new IndexOptions();
        _exclude = _options.ExcludeNamePatterns.Select(LikeToRegex).ToList();

        BuildObjects();

        ColumnsByTable = snapshot.Columns.OrderBy(c => c.ColumnId).ToLookup(c => c.TableName);
        ConstraintsByTable = snapshot.Constraints.ToLookup(c => c.TableName);
        ConstraintColumns = snapshot.ConstraintColumns.OrderBy(c => c.Position).ToLookup(c => (c.Owner, c.ConstraintName));
        IndexesByTable = snapshot.Indexes.Where(i => i.TableOwner == Schema).ToLookup(i => i.TableName);
        IndexColumns = snapshot.IndexColumns.OrderBy(c => c.Position).ToLookup(c => c.IndexName);
        SubprogramsByObject = snapshot.Subprograms.OrderBy(s => s.SubprogramId).ToLookup(s => s.ObjectName);
        ArgumentsBySubprogram = snapshot.Arguments.OrderBy(a => a.Sequence).ToLookup(a => (a.PackageName, a.ObjectName, a.Overload));
        TriggersByTable = snapshot.Triggers.Where(t => t.TableOwner == Schema && t.TableName != null).ToLookup(t => t.TableName!);
        ErrorsByName = snapshot.Errors.OrderBy(e => e.Line).ToLookup(e => e.Name);
        TypeAttributes = snapshot.TypeAttributes.OrderBy(a => a.AttrNo).ToLookup(a => a.TypeName);

        foreach (var c in snapshot.TableComments.Where(c => !string.IsNullOrWhiteSpace(c.Comment)))
            _tableComments.TryAdd(c.TableName, c.Comment.Trim());
        foreach (var c in snapshot.ColumnComments.Where(c => !string.IsNullOrWhiteSpace(c.Comment)))
            _columnComments.TryAdd((c.TableName, c.ColumnName), c.Comment.Trim());
        foreach (var s in snapshot.Sources)
            _sources.TryAdd((s.Name, s.Type), s);

        BuildEdges();
    }

    // ---------- objects ----------

    public string IdFor(ObjectKind kind, string name) => kind switch
    {
        ObjectKind.Trigger => $"{Schema}.{name}~TRG",
        ObjectKind.Job => $"{Schema}.{name}~JOB",
        ObjectKind.DbLink => $"{Schema}.{name}~DBLINK",
        _ => $"{Schema}.{name}",
    };

    public bool IsExcludedName(string name) => _exclude.Any(r => r.IsMatch(name));

    public DbObject? Get(string id) => _byId.GetValueOrDefault(id);

    public DbObject? FindMain(string name) => _mainNamespace.GetValueOrDefault(name);

    public IEnumerable<DbObject> OfKind(ObjectKind kind) =>
        _byId.Values.Where(o => o.Kind == kind).OrderBy(o => o.Name, StringComparer.Ordinal);

    private static ObjectKind? KindOf(string oracleType, bool isMView) => oracleType switch
    {
        "TABLE" => isMView ? ObjectKind.MaterializedView : ObjectKind.Table,
        "VIEW" => ObjectKind.View,
        "MATERIALIZED VIEW" => ObjectKind.MaterializedView,
        "SEQUENCE" => ObjectKind.Sequence,
        "SYNONYM" => ObjectKind.Synonym,
        "PACKAGE" or "PACKAGE BODY" => ObjectKind.Package,
        "PROCEDURE" => ObjectKind.Procedure,
        "FUNCTION" => ObjectKind.Function,
        "TRIGGER" => ObjectKind.Trigger,
        "TYPE" or "TYPE BODY" => ObjectKind.Type,
        "DATABASE LINK" => ObjectKind.DbLink,
        "JOB" => ObjectKind.Job,
        _ => null,
    };

    private void BuildObjects()
    {
        var mviews = Snapshot.MViews.Select(m => m.Name).ToHashSet(StringComparer.Ordinal);

        foreach (var row in Snapshot.Objects)
        {
            if (IsExcludedName(row.Name)) continue;
            var kind = KindOf(row.Type, mviews.Contains(row.Name));
            if (kind is null) continue;

            var id = IdFor(kind.Value, row.Name);
            if (_byId.TryGetValue(id, out var existing))
            {
                // PACKAGE + PACKAGE BODY, TYPE + TYPE BODY, MVIEW + its container TABLE → a single object.
                if (row.Status != "VALID") existing.Status = row.Status;
                if (row.LastDdlTime > existing.LastDdlTime) existing.LastDdlTime = row.LastDdlTime;
                if (row.Created < existing.Created) existing.Created = row.Created;
                continue;
            }

            var obj = new DbObject
            {
                Id = id, Name = row.Name, Kind = kind.Value,
                Status = row.Status, Created = row.Created, LastDdlTime = row.LastDdlTime,
            };
            _byId[id] = obj;
            if (kind == ObjectKind.Trigger) _triggers[row.Name] = obj;
            else if (kind is not (ObjectKind.Job or ObjectKind.DbLink)) _mainNamespace[row.Name] = obj;
        }

        // Private db links are not visible in ALL_OBJECTS (only in DBA_OBJECTS), so take them from ALL_DB_LINKS.
        foreach (var link in Snapshot.DbLinks)
        {
            var id = IdFor(ObjectKind.DbLink, link.Name);
            if (!_byId.ContainsKey(id))
                _byId[id] = new DbObject { Id = id, Name = link.Name, Kind = ObjectKind.DbLink, Created = link.Created };
        }
    }

    // ---------- edges ----------

    public IReadOnlyList<Edge> Outgoing(string id) => _out.TryGetValue(id, out var l) ? l : [];
    public IReadOnlyList<Edge> Incoming(string id) => _in.TryGetValue(id, out var l) ? l : [];

    private void AddEdge(Edge e)
    {
        // self-loops are meaningless (package body → its own spec), except FK: a table referencing itself is a hierarchy
        if (e.ToId == e.FromId && e.Kind != EdgeKind.Fk) return;
        var list = _out.TryGetValue(e.FromId, out var l) ? l : _out[e.FromId] = [];
        if (list.Any(x => x.ToId == e.ToId && x.ToExternal == e.ToExternal && x.Kind == e.Kind && x.Detail == e.Detail)) return;
        list.Add(e);
        Edges.Add(e);
        if (e.ToId is not null)
            (_in.TryGetValue(e.ToId, out var li) ? li : _in[e.ToId] = []).Add(e);
    }

    /// <summary>Returns (internal id | external reference | nothing when the reference should be hidden).</summary>
    private (string? id, ExternalRef? ext) Resolve(string owner, string name, string type, string? dbLink)
    {
        if (dbLink is null && owner == Schema)
        {
            if (IsExcludedName(name)) return (null, null);
            var obj = type == "TRIGGER" ? _triggers.GetValueOrDefault(name) : _mainNamespace.GetValueOrDefault(name);
            return obj is not null ? (obj.Id, null) : (null, new ExternalRef(owner, name, type, null));
        }
        if (dbLink is null && _options.ExcludedRefOwners.Contains(owner)) return (null, null);
        return (null, new ExternalRef(owner, name, type, dbLink));
    }

    private void BuildEdges()
    {
        BuildEdgesRaw();
        // Trigger → its table and synonym → its target also appear in ALL_DEPENDENCIES; keep only the more specific edge.
        var specific = Edges.Where(e => e.Kind is EdgeKind.TriggerOn or EdgeKind.SynonymFor)
            .Select(e => (e.FromId, e.ToId, e.ToExternal)).ToHashSet();
        var redundant = Edges.Where(e => e.Kind == EdgeKind.Depends && specific.Contains((e.FromId, e.ToId, e.ToExternal))).ToHashSet();
        if (redundant.Count == 0) return;
        Edges.RemoveAll(redundant.Contains);
        foreach (var list in _out.Values.Concat(_in.Values)) list.RemoveAll(redundant.Contains);
    }

    private void BuildEdgesRaw()
    {
        foreach (var d in Snapshot.Dependencies)
        {
            var (fromId, _) = Resolve(Schema, d.Name, d.Type, null);
            if (fromId is null) continue;
            // access through the schema's own db link → edge to that db link's note
            if (d.RefDbLink is not null && Get(IdFor(ObjectKind.DbLink, d.RefDbLink)) is { } link)
            {
                AddEdge(new Edge(fromId, link.Id, null, EdgeKind.Depends, $"{d.RefOwner}.{d.RefName}"));
                continue;
            }
            var (toId, ext) = Resolve(d.RefOwner, d.RefName, d.RefType, d.RefDbLink);
            if (toId is null && ext is null) continue;
            AddEdge(new Edge(fromId, toId, ext, EdgeKind.Depends, null));
        }

        foreach (var fk in Snapshot.Constraints.Where(c => c.Type == "R"))
        {
            var from = FindMain(fk.TableName);
            if (from is null || fk.RefTableName is null) continue;
            var (toId, ext) = Resolve(fk.RefOwner ?? Schema, fk.RefTableName, "TABLE", null);
            if (toId is null && ext is null) continue;
            AddEdge(new Edge(from.Id, toId, ext, EdgeKind.Fk, fk.Name));
        }

        foreach (var t in Snapshot.Triggers)
        {
            if (!_triggers.TryGetValue(t.Name, out var trg) || t.TableName is null || t.TableOwner is null) continue;
            var (toId, ext) = Resolve(t.TableOwner, t.TableName, t.BaseObjectType, null);
            if (toId is null && ext is null) continue;
            AddEdge(new Edge(trg.Id, toId, ext, EdgeKind.TriggerOn, t.TriggeringEvent));
        }

        foreach (var s in Snapshot.Synonyms)
        {
            var syn = FindMain(s.Name);
            if (syn is null || s.TableName is null) continue;
            var (toId, ext) = Resolve(s.TableOwner ?? Schema, s.TableName, "UNKNOWN", s.DbLink);
            if (toId is null && ext is null) continue;
            AddEdge(new Edge(syn.Id, toId, ext, EdgeKind.SynonymFor, null));
        }

        foreach (var j in Snapshot.Jobs)
        {
            var job = Get(IdFor(ObjectKind.Job, j.Name));
            if (job is null) continue;
            foreach (var (target, token) in FindReferencedObjects(j.JobAction))
                AddEdge(new Edge(job.Id, target.Id, null, EdgeKind.JobCalls, token));
            if (j.ProgramName is not null && FindMain(StripSchema(j.ProgramName)) is { } prog)
                AddEdge(new Edge(job.Id, prog.Id, null, EdgeKind.JobCalls, j.ProgramName));
        }
    }

    private static readonly Regex IdentifierChain = new(@"[A-Za-z][A-Za-z0-9_$#]*(?:\.[A-Za-z][A-Za-z0-9_$#]*){0,2}", RegexOptions.Compiled);

    /// <summary>Heuristic: which schema objects are mentioned in a text (job_action etc.).</summary>
    public IEnumerable<(DbObject Target, string Token)> FindReferencedObjects(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;
        var seen = new HashSet<string>();
        foreach (Match m in IdentifierChain.Matches(text))
        {
            var parts = m.Value.ToUpperInvariant().Split('.');
            if (parts.Length > 1 && parts[0] == Schema) parts = parts[1..];
            var obj = FindMain(parts[0]);
            if (obj is null || !seen.Add(obj.Id)) continue;
            yield return (obj, m.Value);
        }
    }

    private string StripSchema(string name)
    {
        var up = name.ToUpperInvariant();
        return up.StartsWith(Schema + ".", StringComparison.Ordinal) ? up[(Schema.Length + 1)..] : up;
    }

    // ---------- lookups ----------

    public string? CommentOf(string tableOrView) => _tableComments.GetValueOrDefault(tableOrView);

    public string? ColumnComment(string table, string column) => _columnComments.GetValueOrDefault((table, column));

    public SourceRow? Source(string name, string type) => _sources.GetValueOrDefault((name, type));

    public IEnumerable<ArgumentRow> ArgumentsOf(string? package, string obj, string? overload) =>
        ArgumentsBySubprogram[(package, obj, overload)]
            .Where(a => !(a.ArgumentName is null && a.DataType is null && a.Position != 0)); // "no parameters" row

    public static bool IsFunction(IEnumerable<ArgumentRow> args) => args.Any(a => a.Position == 0);

    private static Regex LikeToRegex(string like)
    {
        var pattern = "^" + string.Join(".*", like.Split('%').Select(Regex.Escape)) + "$";
        return new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);
    }
}
