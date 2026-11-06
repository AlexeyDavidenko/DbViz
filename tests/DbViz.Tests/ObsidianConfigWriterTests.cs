using System.Text.Json.Nodes;
using DbViz.Core;
using DbViz.Obsidian;

namespace DbViz.Tests;

public sealed class ObsidianConfigWriterTests : IDisposable
{
    private readonly string _vault = Path.Combine(Path.GetTempPath(), "dbviz-cfg-" + Guid.NewGuid().ToString("N"));
    private string Cfg(string f) => Path.Combine(_vault, ".obsidian", f);

    public ObsidianConfigWriterTests() => Directory.CreateDirectory(Path.Combine(_vault, ".obsidian"));
    public void Dispose() => Directory.Delete(_vault, recursive: true);

    [Fact]
    public void Keeps_user_settings_and_foreign_groups_and_is_idempotent()
    {
        File.WriteAllText(Cfg("graph.json"), """
            { "search": "my filter", "linkDistance": 250, "colorGroups": [ { "query": "tag:#personal", "color": { "a": 1, "rgb": 1 } }, { "query": "path:\"Docs/Databases/HR\" tag:#layer/storage", "color": { "a": 1, "rgb": 2 } } ] }
            """);
        File.WriteAllText(Cfg("bookmarks.json"), """{ "items": [ { "type": "file", "path": "Note.md", "ctime": 1 } ] }""");

        ObsidianConfigWriter.Apply(_vault, "HR", "Docs/Databases/HR", NullLog.Instance);
        ObsidianConfigWriter.Apply(_vault, "HR", "Docs/Databases/HR", NullLog.Instance);

        var graph = JsonNode.Parse(File.ReadAllText(Cfg("graph.json")))!.AsObject();
        Assert.Equal("my filter", graph["search"]!.GetValue<string>()); // user's filter untouched
        Assert.Equal(250, graph["linkDistance"]!.GetValue<int>());
        Assert.True(graph["showArrow"]!.GetValue<bool>());
        var groups = graph["colorGroups"]!.AsArray();
        Assert.Equal(5, groups.Count); // 1 foreign + 4 layers, no duplicates
        Assert.Equal("tag:#personal", groups[0]!["query"]!.GetValue<string>());

        var items = JsonNode.Parse(File.ReadAllText(Cfg("bookmarks.json")))!["items"]!.AsArray();
        Assert.Equal(2, items.Count); // own bookmark + one group
        var group = items[1]!;
        Assert.Equal("DBVisualization · HR", group["title"]!.GetValue<string>());
        var presets = group["items"]!.AsArray();
        Assert.Equal(8, presets.Count);
        Assert.All(presets, p => Assert.Equal("graph", p!["type"]!.GetValue<string>()));
        Assert.StartsWith("tag:#schema/HR ", presets[0]!["options"]!["search"]!.GetValue<string>());

        Assert.NotEmpty(Directory.GetFiles(Path.Combine(_vault, ".obsidian"), "graph.json.dbviz-backup-*"));
    }

    [Fact]
    public void Creates_files_when_missing()
    {
        ObsidianConfigWriter.Apply(_vault, "HR", null, NullLog.Instance);
        Assert.True(File.Exists(Cfg("graph.json")));
        Assert.True(File.Exists(Cfg("bookmarks.json")));
    }
}
