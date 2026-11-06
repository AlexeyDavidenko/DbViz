using System.Globalization;
using System.Text;
using DbViz.Core.Model;

namespace DbViz.Obsidian;

/// <summary>Schema home page (MOC), per-type indexes and quality reports.</summary>
public sealed class IndexPages(SchemaIndex ix, VaultLayout layout, NoteRenderer notes)
{
    public IEnumerable<Note> All()
    {
        yield return Moc();
        foreach (var kind in Enum.GetValues<ObjectKind>())
            if (ix.OfKind(kind).Any()) yield return KindIndex(kind);
        foreach (var r in Reports()) yield return r;
    }

    private Note Page(string relPath, string title, string id, bool manual = false) =>
        new Note { Id = id, RelativePath = relPath, Title = title, ManualSection = manual }
            .Fm("dbviz_id", id).Fm("schema", ix.Schema).Fm("tags", new List<string> { "dbviz", $"schema/{ix.Schema}" });

    // ---------------------------------------------------------------- MOC

    private Note Moc()
    {
        var s = ix.Snapshot;
        var note = Page($"{layout.MocName}.md", $"{ix.Schema} schema", $"page:{ix.Schema}", manual: true);

        note.Block("summary", Md.Bullets(new[]
        {
            $"**Extracted:** {s.ExtractedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}",
            $"**Database:** {s.DbVersion ?? "—"}",
            $"**Objects:** {ix.Objects.Count}, **relationships:** {ix.Edges.Count}",
            $"**INVALID:** {ix.Objects.Count(o => !o.IsValid)}",
        }));

        var sb = new StringBuilder("## Layers\n");
        foreach (var layer in Enum.GetValues<Layer>())
        {
            var kinds = Enum.GetValues<ObjectKind>().Where(k => KindInfo.LayerOf(k) == layer && ix.OfKind(k).Any()).ToList();
            if (kinds.Count == 0) continue;
            sb.Append($"\n### {KindInfo.LayerTitle(layer)}\n\n");
            sb.Append(Md.Table(["Type", "Count", "INVALID", "Undocumented"], kinds.Select(k =>
            {
                var list = ix.OfKind(k).ToList();
                return new[]
                {
                    $"[[{KindIndexName(k)}|{KindInfo.Plural(k)}]]", list.Count.ToString(),
                    list.Count(o => !o.IsValid) is var inv and > 0 ? inv.ToString() : "",
                    list.Count(o => notes.Describe(o).Text is null).ToString(),
                };
            }))).Append('\n');
        }
        note.Block("layers", sb.ToString().TrimEnd());

        note.Block("reports", "## Reports\n\n" + Md.Bullets(ReportTitles.Select(t => $"[[{ReportName(t)}|{t}]]")));

        note.Block("warnings", s.Warnings.Count == 0 ? "" :
            "## Extraction warnings\n\n> [!warning] This data could not be retrieved, so some information may be missing\n\n" +
            Md.Table(["Query", "Error"], s.Warnings.Select(w => new[] { w.Query, w.Message })));

        note.Block("graph", GraphHelp());
        return note;
    }

    private string GraphHelp()
    {
        var presets = GraphPresets.For(ix.Schema);
        return "## Graph by layer\n\n" +
               $"Ready-made graph views are in **Bookmarks → \"{ObsidianConfigWriter.BookmarkGroupTitle(ix.Schema)}\"**, " +
               "layer colors are in the graph settings (Groups). Both are set up by `dbviz graph-config`.\n\n" +
               "Manually: Graph view → **Filters** → type a query. Only matching notes and the links " +
               "**between them** stay; turn **Orphans** off and **Display → Arrows** on to see dependency direction.\n\n" +
               Md.Table(["View", "Filters query", "What you see"], presets.Select(x => new[] { x.Title, Md.Code(x.Query), x.What })) +
               "\n\n**Hide a layer**: prefix a condition with a minus, `-tag:#layer/access`. **One object and its neighbours**: open its note and use " +
               "\"Open local graph\" (depth 1–2); the same filters work there.\n\n" +
               "**Layer colors:**\n\n" +
               Md.Table(["Layer", "Group query", "Color"], GraphPresets.LayerColors.Select(c => new[]
               {
                   KindInfo.LayerTitle(c.Layer), Md.Code(GraphPresets.LayerQuery(ix.Schema, c.Layer)), c.Hex,
               }));
    }

    // ---------------------------------------------------------------- indexes

    private string KindIndexName(ObjectKind k) => VaultLayout.Sanitize($"{ix.Schema} — {KindInfo.Plural(k)}");

    private Note KindIndex(ObjectKind kind)
    {
        var name = KindIndexName(kind);
        var note = Page($"{VaultLayout.IndexFolder}/{name}.md", $"{KindInfo.Plural(kind)} of the {ix.Schema} schema", $"page:{ix.Schema}:index:{kind}");
        var list = ix.OfKind(kind).ToList();
        var withRows = kind == ObjectKind.Table;
        var headers = withRows ? new[] { "Object", "Status", "Rows", "Description" } : ["Object", "Status", "Description"];
        var rows = list.Select(o =>
        {
            var cells = new List<string?> { layout.Link(o), o.IsValid ? "" : "**" + o.Status + "**" };
            if (withRows) cells.Add(ix.Snapshot.Tables.FirstOrDefault(t => t.Name == o.Name)?.NumRows?.ToString("N0", CultureInfo.GetCultureInfo("ru-RU")));
            cells.Add(Md.Truncate(notes.Describe(o).Text, 140));
            return (IReadOnlyList<string?>)cells;
        });
        note.Block("content", $"[[{layout.MocName}]] · {KindInfo.LayerTitle(KindInfo.LayerOf(kind))} · total: {list.Count}\n\n" + Md.Table(headers, rows));
        return note;
    }

    // ---------------------------------------------------------------- reports

    private static readonly string[] ReportTitles =
    [
        "Invalid objects", "Tables without a primary key", "FK without an index", "Undocumented objects",
        "Unused objects", "External dependencies",
    ];

    private string ReportName(string title) => VaultLayout.Sanitize($"{ix.Schema} — {title}");

    private Note Report(string title, string content)
    {
        var note = Page($"{VaultLayout.ReportsFolder}/{ReportName(title)}.md", title, $"page:{ix.Schema}:report:{title}");
        note.Block("content", $"[[{layout.MocName}]]\n\n" + (string.IsNullOrWhiteSpace(content) ? "_Nothing found._ ✅" : content));
        return note;
    }

    private IEnumerable<Note> Reports()
    {
        // 1. INVALID
        yield return Report(ReportTitles[0], Md.Table(["Object", "Type", "Status", "Errors", "First error"],
            ix.Objects.Where(o => !o.IsValid).OrderBy(o => o.Kind).ThenBy(o => o.Name).Select(o =>
            {
                var errs = ix.ErrorsByName[o.Name].ToList();
                return new[] { layout.Link(o), KindInfo.Title(o.Kind), o.Status, errs.Count.ToString(), Md.Truncate(errs.FirstOrDefault()?.Text, 120) };
            })));

        // 2. no primary key
        yield return Report(ReportTitles[1], Md.Table(["Table", "Rows", "Has UK"],
            ix.OfKind(ObjectKind.Table)
                .Where(t => !ix.ConstraintsByTable[t.Name].Any(c => c.Type == "P"))
                .Where(t => ix.Snapshot.Tables.FirstOrDefault(x => x.Name == t.Name)?.Temporary != true)
                .Select(t => new[]
                {
                    layout.Link(t),
                    ix.Snapshot.Tables.FirstOrDefault(x => x.Name == t.Name)?.NumRows?.ToString("N0", CultureInfo.GetCultureInfo("ru-RU")),
                    ix.ConstraintsByTable[t.Name].Any(c => c.Type == "U") ? "yes" : "",
                })));

        // 3. FK without an index: FK columns are not the leading columns of any index
        yield return Report(ReportTitles[2],
            "Without an index on the FK, deleting/updating a parent row locks the whole child table and causes a full scan.\n\n" +
            Md.Table(["Table", "FK", "Columns", "→ Table"], ix.Snapshot.Constraints
                .Where(c => c.Type == "R" && ix.FindMain(c.TableName) is not null)
                .Where(c => !HasSupportingIndex(c))
                .OrderBy(c => c.TableName).ThenBy(c => c.Name)
                .Select(c => new[]
                {
                    layout.Link(ix.FindMain(c.TableName)!), c.Name,
                    string.Join(", ", ix.ConstraintColumns[(ix.Schema, c.Name)].Select(x => x.ColumnName)),
                    c.RefOwner == ix.Schema && c.RefTableName is not null && ix.FindMain(c.RefTableName) is { } t ? layout.Link(t) : $"{c.RefOwner}.{c.RefTableName}",
                })));

        // 4. no description
        var described = new[] { ObjectKind.Table, ObjectKind.View, ObjectKind.MaterializedView, ObjectKind.Package, ObjectKind.Procedure, ObjectKind.Function };
        var sb = new StringBuilder();
        foreach (var k in described)
        {
            var list = ix.OfKind(k).Where(o => notes.Describe(o).Text is null).ToList();
            if (list.Count == 0) continue;
            sb.Append($"### {KindInfo.Plural(k)} ({list.Count} of {ix.OfKind(k).Count()})\n\n{string.Join(", ", list.Select(o => layout.Link(o)))}\n\n");
        }
        var colsTotal = ix.Snapshot.Columns.Count(c => ix.FindMain(c.TableName)?.Kind == ObjectKind.Table);
        var colsDescribed = ix.Snapshot.Columns.Count(c => ix.FindMain(c.TableName)?.Kind == ObjectKind.Table && ix.ColumnComment(c.TableName, c.Name) is not null);
        if (colsTotal > 0) sb.Insert(0, $"Table columns with a comment: **{colsDescribed} of {colsTotal}** ({100.0 * colsDescribed / colsTotal:F0}%).\n\n");
        yield return Report(ReportTitles[3], sb.ToString().TrimEnd());

        // 5. unused
        var candidates = ix.Objects
            .Where(o => o.Kind is ObjectKind.Table or ObjectKind.View or ObjectKind.MaterializedView or ObjectKind.Procedure
                or ObjectKind.Function or ObjectKind.Package or ObjectKind.Sequence or ObjectKind.Type)
            .Where(o => ix.Incoming(o.Id).Count == 0)
            .OrderBy(o => o.Kind).ThenBy(o => o.Name).ToList();
        yield return Report(ReportTitles[4],
            "> [!warning] Candidates, not a verdict\n> According to the dictionary nothing **inside the schema** references these objects. " +
            "They may still be used by external applications, other schemas, dynamic SQL (`EXECUTE IMMEDIATE`) or reports.\n\n" +
            Md.Table(["Object", "Type", "Last DDL"], candidates.Select(o => new[]
            {
                layout.Link(o), KindInfo.Title(o.Kind), o.LastDdlTime?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            })));

        // 6. external dependencies
        var ext = ix.Edges.Where(e => e.ToExternal is not null)
            .GroupBy(e => e.ToExternal!.Display).OrderBy(g => g.Key, StringComparer.Ordinal);
        yield return Report(ReportTitles[5], Md.Table(["External object", "Type", "Used by"], ext.Select(g => new[]
        {
            Md.Code(g.Key), g.First().ToExternal!.Type,
            string.Join(", ", g.Select(e => ix.Get(e.FromId)).Where(o => o is not null).Select(o => layout.Link(o!)).Distinct()),
        })));
    }

    private bool HasSupportingIndex(ConstraintRow fk)
    {
        var fkCols = ix.ConstraintColumns[(ix.Schema, fk.Name)].Select(c => c.ColumnName).ToHashSet();
        if (fkCols.Count == 0) return true;
        return ix.IndexesByTable[fk.TableName].Any(i =>
        {
            var lead = ix.IndexColumns[i.Name].Take(fkCols.Count).Select(c => c.ColumnName).ToHashSet();
            return lead.SetEquals(fkCols);
        });
    }
}
