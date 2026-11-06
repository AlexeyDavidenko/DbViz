using System.IO.Compression;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DbViz.Core;
using DbViz.Core.Demo;
using DbViz.Obsidian;

namespace DbViz.Tests;

public sealed class StandaloneExporterTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "dbviz-export-" + Guid.NewGuid().ToString("N"));
    public void Dispose() => Directory.Delete(_tmp, recursive: true);

    [Fact]
    public void Export_produces_self_contained_vault_with_owner_edits_bookmarks_and_zip()
    {
        // owner's vault with a manual edit
        var owner = Path.Combine(_tmp, "owner");
        ObsidianRenderer.Render(DemoSnapshot.Create(), owner, "Docs/Databases", null, NullLog.Instance);
        var ownerOrders = Path.Combine(owner, "Docs/Databases/DEMO/10 Tables/DEMO.ORDERS.md");
        File.WriteAllText(ownerOrders, File.ReadAllText(ownerOrders).Replace(Note.ManualHeading + "\n", Note.ManualHeading + "\nPartitioned by month.\n"));

        var outDir = Path.Combine(_tmp, "out");
        var r = StandaloneExporter.Export(DemoSnapshot.Create(), Path.Combine(owner, "Docs/Databases/DEMO"), outDir, null, NullLog.Instance);
        Assert.Equal(1, r.ManualNotesCarried);

        var schema = Path.Combine(r.VaultDir, StandaloneExporter.Section, "DEMO");
        Assert.Contains("Partitioned by month.", File.ReadAllText(Path.Combine(schema, "10 Tables/DEMO.ORDERS.md")));

        // presets do not depend on the path and do not reference the owner's personal vault
        var moc = File.ReadAllText(Path.Combine(schema, "DEMO.md"));
        Assert.Contains("tag:#schema/DEMO", moc);
        Assert.DoesNotContain("Docs/Databases", moc);

        // Obsidian settings
        var cfg = Path.Combine(r.VaultDir, ".obsidian");
        var graph = JsonNode.Parse(File.ReadAllText(Path.Combine(cfg, "graph.json")))!;
        Assert.Equal(4, graph["colorGroups"]!.AsArray().Count);
        Assert.True(graph["showArrow"]!.GetValue<bool>());
        var items = JsonNode.Parse(File.ReadAllText(Path.Combine(cfg, "bookmarks.json")))!["items"]!.AsArray();
        Assert.Equal(StandaloneExporter.HomeNote, items[0]!["path"]!.GetValue<string>());
        Assert.Equal("DBVisualization/DEMO/DEMO.md", items[1]!["path"]!.GetValue<string>());
        Assert.Equal(8, items[2]!["items"]!.AsArray().Count);
        Assert.True(JsonNode.Parse(File.ReadAllText(Path.Combine(cfg, "core-plugins.json")))!["bookmarks"]!.GetValue<bool>());

        // every link inside the exported vault resolves
        var names = Directory.EnumerateFiles(r.VaultDir, "*.md", SearchOption.AllDirectories)
            .Select(Path.GetFileNameWithoutExtension).ToHashSet();
        var link = new Regex(@"\[\[([^\]|#\\]+)");
        var broken = Directory.EnumerateFiles(r.VaultDir, "*.md", SearchOption.AllDirectories)
            .SelectMany(f => link.Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value.Trim()))
            .Where(t => !names.Contains(t)).Distinct().ToList();
        Assert.Empty(broken);

        // the archive contains the whole vault
        using var zip = ZipFile.OpenRead(r.ZipPath);
        Assert.Contains(zip.Entries, e => e.FullName.Replace('\\', '/') == "DbViz-DEMO/.obsidian/bookmarks.json");
        Assert.Contains(zip.Entries, e => e.FullName.Replace('\\', '/') == "DbViz-DEMO/" + StandaloneExporter.HomeNote);

        // re-export recreates its own folder without errors
        StandaloneExporter.Export(DemoSnapshot.Create(), null, outDir, null, NullLog.Instance);
    }

    [Fact]
    public void Refuses_to_overwrite_foreign_folder()
    {
        var outDir = Path.Combine(_tmp, "out");
        Directory.CreateDirectory(Path.Combine(outDir, "DbViz-DEMO"));
        File.WriteAllText(Path.Combine(outDir, "DbViz-DEMO", "important.md"), "x");
        Assert.Throws<InvalidOperationException>(() => StandaloneExporter.Export(DemoSnapshot.Create(), null, outDir, null, NullLog.Instance));
        Assert.True(File.Exists(Path.Combine(outDir, "DbViz-DEMO", "important.md")));
    }

    [Fact]
    public void Export_contains_installer_for_existing_vault()
    {
        var r = StandaloneExporter.Export(DemoSnapshot.Create(), null, Path.Combine(_tmp, "out"), null, NullLog.Instance);
        var inst = Path.Combine(r.VaultDir, StandaloneExporter.InstallFolder);
        var payload = JsonNode.Parse(File.ReadAllText(Path.Combine(inst, "dbviz-obsidian-settings.json")))!;
        Assert.Equal(4, payload["colorGroups"]!.AsArray().Count);
        Assert.Equal(8, payload["bookmarkGroup"]!["items"]!.AsArray().Count);
        Assert.True(File.Exists(Path.Combine(r.VaultDir, StandaloneExporter.ReadmeFile)));

        var ps1 = File.ReadAllBytes(Path.Combine(inst, "install.ps1"));
        Assert.True(ps1[0] == 0xEF && ps1[1] == 0xBB && ps1[2] == 0xBF, "install.ps1 must have a BOM for Windows PowerShell 5.1");
        var mac = Path.Combine(inst, StandaloneExporter.MacInstaller);
        if (!OperatingSystem.IsWindows())
            Assert.True(File.GetUnixFileMode(mac).HasFlag(UnixFileMode.UserExecute));

        using var zip = ZipFile.OpenRead(r.ZipPath);
        var entry = zip.Entries.Single(e => e.FullName.EndsWith("(macOS).command", StringComparison.Ordinal));
        if (!OperatingSystem.IsWindows()) // Unix permissions are only stored when the archive is built on Unix
            Assert.True(((entry.ExternalAttributes >> 16) & 0x40) != 0, ".command must stay executable inside the zip");
    }

    [Fact]
    public void Mac_installer_merges_into_existing_vault()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var r = StandaloneExporter.Export(DemoSnapshot.Create(), null, Path.Combine(_tmp, "out"), null, NullLog.Instance);

        // someone else's vault: own filter, own color group, legacy DbViz group, own bookmark, core-plugins in the legacy (array) format
        var vault = Path.Combine(_tmp, "colleague vault");
        var cfg = Path.Combine(vault, ".obsidian");
        Directory.CreateDirectory(cfg);
        File.WriteAllText(Path.Combine(cfg, "graph.json"), """{ "search": "my filter", "colorGroups": [ { "query": "tag:#home", "color": { "a": 1, "rgb": 5 } }, { "query": "path:\"X\" tag:#schema/DEMO tag:#layer/logic", "color": { "a": 1, "rgb": 6 } } ] }""");
        File.WriteAllText(Path.Combine(cfg, "bookmarks.json"), """{ "items": [ { "type": "file", "path": "Home.md", "ctime": 1 } ] }""");
        File.WriteAllText(Path.Combine(cfg, "core-plugins.json"), """["file-explorer","graph"]""");

        var inst = Path.Combine(r.VaultDir, StandaloneExporter.InstallFolder);
        var psi = new System.Diagnostics.ProcessStartInfo("osascript", ["-l", "JavaScript", Path.Combine(inst, "install.js"), inst, vault, "--notes", "--yes"])
            { RedirectStandardOutput = true, RedirectStandardError = true };
        using (var p = System.Diagnostics.Process.Start(psi)!)
        {
            var err = p.StandardError.ReadToEnd();
            p.WaitForExit();
            Assert.True(p.ExitCode == 0, err);
        }

        var graph = JsonNode.Parse(File.ReadAllText(Path.Combine(cfg, "graph.json")))!;
        Assert.Equal("my filter", graph["search"]!.GetValue<string>());
        var queries = graph["colorGroups"]!.AsArray().Select(g => g!["query"]!.GetValue<string>()).ToList();
        Assert.Equal(5, queries.Count); // tag:#home (foreign) + 4 layers; the old DbViz group (tag:#schema/DEMO ...) is replaced, not duplicated
        Assert.Contains("tag:#home", queries);
        Assert.Single(queries, q => q.EndsWith("tag:#layer/logic", StringComparison.Ordinal));
        Assert.True(graph["showArrow"]!.GetValue<bool>());

        var items = JsonNode.Parse(File.ReadAllText(Path.Combine(cfg, "bookmarks.json")))!["items"]!.AsArray();
        Assert.Equal("Home.md", items[0]!["path"]!.GetValue<string>());
        Assert.Equal("DBVisualization · DEMO", items[1]!["title"]!.GetValue<string>());

        var core = JsonNode.Parse(File.ReadAllText(Path.Combine(cfg, "core-plugins.json")))!.AsArray().Select(x => x!.GetValue<string>()).ToList();
        Assert.Contains("bookmarks", core);
        Assert.Contains("file-explorer", core);

        Assert.True(File.Exists(Path.Combine(vault, "DBVisualization", "DEMO", "DEMO.md")));
        Assert.NotEmpty(Directory.GetFiles(cfg, "graph.json.dbviz-backup-*"));
    }
}
