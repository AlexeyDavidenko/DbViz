using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using DbViz.Core;

namespace DbViz.Obsidian;

/// <summary>
/// Layer colors and graph presets in Obsidian settings (.obsidian/graph.json and bookmarks.json).
/// Foreign keys, color groups and bookmarks are preserved; a backup is made before changes; re-running never duplicates anything.
/// The same data is shipped in the export archive as the payload for the installer into someone else's vault.
/// </summary>
public static class ObsidianConfigWriter
{
    internal static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static readonly string[] RequiredCorePlugins = ["graph", "bookmarks", "backlink", "outgoing-link", "tag-pane", "canvas", "properties", "page-preview"];

    public static string BookmarkGroupTitle(string schema) => $"DBVisualization · {schema}";

    /// <param name="legacySectionPath">Section path, used to remove legacy-format groups (path:"...") if any are left.</param>
    public static void Apply(string vaultRoot, string schema, string? legacySectionPath, ILog log)
    {
        var dir = Path.Combine(vaultRoot, ".obsidian");
        if (!Directory.Exists(dir)) throw new DirectoryNotFoundException($"Obsidian settings folder not found: {dir}");

        var graphPath = Path.Combine(dir, "graph.json");
        var graph = Load(graphPath) ?? new JsonObject();
        MergeGraph(graph, schema, legacySectionPath);
        Save(graphPath, graph, log);

        var bmPath = Path.Combine(dir, "bookmarks.json");
        var bookmarks = Load(bmPath) ?? new JsonObject();
        MergeBookmarks(bookmarks, schema, graph);
        Save(bmPath, bookmarks, log);
    }

    /// <summary>Complete settings for a standalone vault.</summary>
    public static void WriteStandalone(string vaultRoot, string schema, IReadOnlyList<string> fileBookmarks)
    {
        var dir = Path.Combine(vaultRoot, ".obsidian");
        Directory.CreateDirectory(dir);

        var core = new JsonObject();
        foreach (var p in new[] { "file-explorer", "global-search", "switcher", "command-palette", "outline", "word-count", "file-recovery", "bases" }
                     .Concat(RequiredCorePlugins))
            core[p] = true;
        File.WriteAllText(Path.Combine(dir, "core-plugins.json"), core.ToJsonString(Json));
        File.WriteAllText(Path.Combine(dir, "community-plugins.json"), "[]");
        File.WriteAllText(Path.Combine(dir, "app.json"), new JsonObject { ["readableLineLength"] = false }.ToJsonString(Json));

        var graph = DefaultGraph(schema);
        MergeGraph(graph, schema, null);
        File.WriteAllText(Path.Combine(dir, "graph.json"), graph.ToJsonString(Json));

        var items = new JsonArray();
        var now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        foreach (var f in fileBookmarks) items.Add(new JsonObject { ["type"] = "file", ["ctime"] = now, ["path"] = f });
        var bookmarks = new JsonObject { ["items"] = items };
        MergeBookmarks(bookmarks, schema, graph);
        File.WriteAllText(Path.Combine(dir, "bookmarks.json"), bookmarks.ToJsonString(Json));
    }

    /// <summary>Payload for the installer into an existing vault (dbviz-obsidian-settings.json).</summary>
    public static JsonObject InstallerPayload(string schema, string notesFolder)
    {
        var graph = DefaultGraph(schema);
        MergeGraph(graph, schema, null);
        var bookmarks = new JsonObject();
        MergeBookmarks(bookmarks, schema, graph);
        return new JsonObject
        {
            ["schema"] = schema,
            ["notesFolder"] = notesFolder,
            ["groupMarkers"] = new JsonArray(GraphPresets.OwnGroupMarkers(schema, null).Select(m => (JsonNode)m).ToArray()),
            ["colorGroups"] = graph["colorGroups"]!.DeepClone(),
            ["bookmarkGroup"] = ((JsonArray)bookmarks["items"]!)[0]!.DeepClone(),
            ["corePlugins"] = new JsonArray(RequiredCorePlugins.Select(p => (JsonNode)p).ToArray()),
        };
    }

    private static JsonObject DefaultGraph(string schema) => new()
    {
        ["collapse-filter"] = false,
        ["search"] = GraphPresets.For(schema)[0].Query,
        ["showTags"] = false, ["showAttachments"] = false, ["hideUnresolved"] = true, ["showOrphans"] = false,
        ["collapse-color-groups"] = false, ["colorGroups"] = new JsonArray(),
        ["collapse-display"] = true, ["showArrow"] = true,
        ["textFadeMultiplier"] = 0, ["nodeSizeMultiplier"] = 1, ["lineSizeMultiplier"] = 1,
        ["collapse-forces"] = true, ["centerStrength"] = 0.5, ["repelStrength"] = 10, ["linkStrength"] = 1, ["linkDistance"] = 250,
        ["scale"] = 0.5, ["close"] = false,
    };

    /// <summary>Layer color groups (only our own are replaced), arrows enabled. The user's filter is left untouched.</summary>
    public static void MergeGraph(JsonObject graph, string schema, string? legacySectionPath)
    {
        var markers = GraphPresets.OwnGroupMarkers(schema, legacySectionPath).ToList();
        var groups = graph["colorGroups"] as JsonArray ?? [];
        var result = new JsonArray();
        foreach (var g in groups)
        {
            var q = g?["query"]?.GetValue<string>() ?? "";
            if (!markers.Any(m => q.Contains(m, StringComparison.Ordinal))) result.Add(g?.DeepClone());
        }
        foreach (var (layer, hex) in GraphPresets.LayerColors) result.Add(ColorGroup(GraphPresets.LayerQuery(schema, layer), hex));
        graph["colorGroups"] = result;
        graph["showArrow"] = true;
    }

    /// <summary>Bookmark group with graph presets (recreated as a whole; other bookmarks are untouched).</summary>
    public static void MergeBookmarks(JsonObject bookmarks, string schema, JsonObject graph)
    {
        var title = BookmarkGroupTitle(schema);
        var items = bookmarks["items"] as JsonArray ?? [];
        bool IsOurs(JsonNode? i) => i?["type"]?.GetValue<string>() == "group" && i["title"]?.GetValue<string>() == title;
        var others = items.Where(i => !IsOurs(i)).Select(i => i?.DeepClone()).ToList();

        // keep the group creation time, otherwise every run would change the file
        var now = items.FirstOrDefault(IsOurs)?["ctime"]?.GetValue<long>() ?? DateTimeOffset.Now.ToUnixTimeMilliseconds();
        var group = new JsonObject { ["type"] = "group", ["ctime"] = now, ["title"] = title, ["items"] = new JsonArray() };
        foreach (var p in GraphPresets.For(schema))
            ((JsonArray)group["items"]!).Add(new JsonObject { ["type"] = "graph", ["ctime"] = now, ["options"] = GraphOptions(graph, p.Query), ["title"] = p.Title });

        var result = new JsonArray();
        foreach (var o in others) result.Add(o);
        result.Add(group);
        bookmarks["items"] = result;
    }

    /// <summary>Graph options for a bookmark: current settings + preset filter, no orphans, arrows, layer colors.</summary>
    private static JsonObject GraphOptions(JsonObject graph, string query)
    {
        var o = new JsonObject();
        foreach (var (k, v) in graph)
            if (!k.StartsWith("collapse-", StringComparison.Ordinal) && k is not ("close" or "scale")) o[k] = v?.DeepClone();
        o["search"] = query;
        o["showOrphans"] = false;
        o["showArrow"] = true;
        o["showTags"] = false;
        o["showAttachments"] = false;
        o["hideUnresolved"] = true;
        return o;
    }

    private static JsonObject ColorGroup(string query, string hex) =>
        new() { ["query"] = query, ["color"] = new JsonObject { ["a"] = 1, ["rgb"] = GraphPresets.Rgb(hex) } };

    private static JsonObject? Load(string path) =>
        File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject : null;

    private static void Save(string path, JsonObject node, ILog log)
    {
        var text = node.ToJsonString(Json);
        if (File.Exists(path))
        {
            if (File.ReadAllText(path) == text) { log.Info($"  {Path.GetFileName(path)}: unchanged"); return; }
            var backup = $"{path}.dbviz-backup-{DateTime.Now:yyyyMMdd-HHmmss-fff}";
            File.Copy(path, backup);
            log.Info($"  backup: {backup}");
        }
        File.WriteAllText(path, text);
        log.Info($"  written {path}");
    }
}
