using DbViz.Core.Model;

namespace DbViz.Obsidian;

/// <summary>
/// Graph view filter presets and layer colors: single source for the schema MOC, .obsidian (graph-config), export and the installer.
/// Queries use the schema tag (<c>tag:#schema/HR</c>) rather than a path, so they work wherever the notes folder is placed.
/// </summary>
public static class GraphPresets
{
    public sealed record Preset(string Title, string Query, string What);

    public static readonly (Layer Layer, string Hex)[] LayerColors =
    [
        (Layer.Storage, "#3b82f6"), (Layer.Access, "#22c55e"), (Layer.Logic, "#f97316"), (Layer.Integration, "#a855f7"),
    ];

    /// <summary>All notes of the schema (objects + service pages).</summary>
    public static string SchemaFilter(string schema) => $"tag:#schema/{schema}";

    public static string LayerQuery(string schema, Layer layer) => $"{SchemaFilter(schema)} tag:#{KindInfo.LayerTag(layer)}";

    /// <summary>Markers of our own color groups in graph.json (incl. the legacy path: format): those are replaced, others are kept.</summary>
    public static IEnumerable<string> OwnGroupMarkers(string schema, string? legacySectionPath)
    {
        yield return SchemaFilter(schema) + " ";
        if (legacySectionPath is not null) yield return $"path:\"{legacySectionPath}\"";
    }

    public static IReadOnlyList<Preset> For(string schema)
    {
        var s = SchemaFilter(schema);
        // service pages (MOC, indexes, reports) are tagged #dbviz and excluded, otherwise they pull the graph into hubs
        var basis = $"{s} -tag:#dbviz";
        return
        [
            new("Whole schema", basis, "all objects and all relationships"),
            new("Tables only", $"{s} tag:#db/table", "data model: FK relationships"),
            new("Tables + views", $"{s} (tag:#db/table OR tag:#db/view)", "which views are built on which tables + FK"),
            new("Logic + tables", $"{s} (tag:#layer/logic OR tag:#db/table)", "which packages and triggers touch which tables"),
            new("Logic + views", $"{s} (tag:#layer/logic OR tag:#db/view)", "which packages read which views"),
            new("Everything but views", $"{basis} -tag:#db/view", "hide layer L1 to declutter the graph"),
            new("Triggers and their tables", $"{s} (tag:#db/trigger OR tag:#db/table)", "which tables have triggers and where they write"),
            new("Packages only", $"{s} tag:#db/package", "package → package calls"),
        ];
    }

    public static int Rgb(string hex) => Convert.ToInt32(hex.TrimStart('#'), 16);
}
