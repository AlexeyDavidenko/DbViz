using System.Text.RegularExpressions;
using DbViz.Core;
using DbViz.Core.Demo;
using DbViz.Obsidian;

namespace DbViz.Tests;

public sealed class RenderTests : IDisposable
{
    private readonly string _vault = Path.Combine(Path.GetTempPath(), "dbviz-tests-" + Guid.NewGuid().ToString("N"));
    private string SchemaRoot => Path.Combine(_vault, "Sec", "DEMO");

    public void Dispose()
    {
        if (Directory.Exists(_vault)) Directory.Delete(_vault, recursive: true);
    }

    private RenderSummary Render(DbViz.Core.Model.Snapshot? s = null) =>
        ObsidianRenderer.Render(s ?? DemoSnapshot.Create(), _vault, "Sec", null, NullLog.Instance);

    private static readonly Regex WikiLink = new(@"\[\[([^\]|#\\]+)(?:#([^\]|\\]+))?(?:\\?\|[^\]]*)?\]\]");

    [Fact]
    public void Every_wikilink_resolves_to_a_note_and_heading()
    {
        Render();
        var files = Directory.EnumerateFiles(SchemaRoot, "*.md", SearchOption.AllDirectories).ToList();
        var byName = files.ToDictionary(f => Path.GetFileNameWithoutExtension(f), f => File.ReadAllText(f));

        var broken = new List<string>();
        foreach (var (name, text) in byName)
        foreach (Match m in WikiLink.Matches(text))
        {
            var target = m.Groups[1].Value.Trim();
            if (!byName.TryGetValue(target, out var targetText)) { broken.Add($"{name} → {target}"); continue; }
            if (m.Groups[2].Success && !targetText.Contains("\n### " + m.Groups[2].Value.Trim() + "\n"))
                broken.Add($"{name} → {target}#{m.Groups[2].Value}");
        }
        Assert.Empty(broken);
    }

    [Fact]
    public void Second_render_changes_nothing()
    {
        var first = Render();
        Assert.True(first.Created > 30);
        var second = Render();
        Assert.Equal(0, second.Created);
        Assert.Equal(0, second.Updated);
    }

    [Fact]
    public void Manual_edits_survive_and_dropped_objects_are_archived_not_deleted()
    {
        Render();
        var orders = Path.Combine(SchemaRoot, "10 Tables", "DEMO.ORDERS.md");
        File.WriteAllText(orders, File.ReadAllText(orders).Replace(Note.ManualHeading + "\n", Note.ManualHeading + "\nImportant: partitioned by month.\n"));

        var s = DemoSnapshot.Create();
        s.Objects.RemoveAll(o => o.Name == "AUDIT_LOG");
        s.TableComments.RemoveAll(c => c.TableName == "ORDERS");
        s.TableComments.Add(new("ORDERS", "TABLE", "Orders (new description)"));
        Render(s);

        var text = File.ReadAllText(orders);
        Assert.Contains("Important: partitioned by month.", text);
        Assert.Contains("Orders (new description)", text);
        Assert.False(File.Exists(Path.Combine(SchemaRoot, "10 Tables", "DEMO.AUDIT_LOG.md")));
        Assert.True(File.Exists(Path.Combine(SchemaRoot, VaultLayout.ArchiveFolder, "DEMO.AUDIT_LOG.md")));
    }

    [Fact]
    public void Files_outside_schema_section_are_untouched()
    {
        Directory.CreateDirectory(Path.Combine(_vault, "Sec"));
        var foreign = Path.Combine(_vault, "Sec", "My note.md");
        File.WriteAllText(foreign, "---\ndbviz_id: \"DEMO.GHOST\"\n---\ndo not touch");
        var userNote = Path.Combine(SchemaRoot, "My thoughts.md");
        Directory.CreateDirectory(SchemaRoot);
        File.WriteAllText(userNote, "user note without dbviz_id");

        Render();

        Assert.Equal("---\ndbviz_id: \"DEMO.GHOST\"\n---\ndo not touch", File.ReadAllText(foreign));
        Assert.Equal("user note without dbviz_id", File.ReadAllText(userNote));
    }

    [Fact]
    public void Notes_contain_expected_content()
    {
        Render();
        var orders = File.ReadAllText(Path.Combine(SchemaRoot, "10 Tables", "DEMO.ORDERS.md"));
        Assert.Contains("FK → [[DEMO.CUSTOMERS\\|CUSTOMERS]].CUSTOMER_ID", orders);
        Assert.Contains("Total \\| after discounts", orders);
        Assert.DoesNotContain("SYS_C0012345", orders); // NOT NULL check hidden
        Assert.Contains("erDiagram", orders);

        var pkg = File.ReadAllText(Path.Combine(SchemaRoot, "30 Packages", "DEMO.PKG_ORDERS.md"));
        Assert.Contains("### CANCEL_ORDER (overload 2)", pkg);
        Assert.Contains("[[DEMO.T_ORDER_LINES\\|T_ORDER_LINES]]", pkg);
        Assert.Contains("RETURN NUMBER DETERMINISTIC", pkg);

        var fn = File.ReadAllText(Path.Combine(SchemaRoot, "32 Functions", "DEMO.FN_FORMAT_NAME.md"));
        Assert.Contains("status/invalid", fn);
        Assert.Contains("PLS-00201", fn);

        var noIdx = File.ReadAllText(Path.Combine(SchemaRoot, "_reports", "DEMO — FK without an index.md"));
        Assert.Contains("FK_ORDERS_CUSTOMER", noIdx);
        Assert.DoesNotContain("FK_ITEMS_ORDER", noIdx); // covered by PK_ORDER_ITEMS(ORDER_ID, LINE_NO)
    }

    [Fact]
    public void Legacy_russian_manual_section_is_migrated_and_stale_pages_are_archived()
    {
        Render();
        // simulate a note and a report page written by an earlier (Russian) version
        var orders = Path.Combine(SchemaRoot, "10 Tables", "DEMO.ORDERS.md");
        var legacy = File.ReadAllText(orders).Replace(Note.ManualHeading + "\n" + Note.ManualPlaceholder,
            Note.LegacyManualHeadings[0] + "\n" + Note.LegacyManualPlaceholders[0] + "\nkeep me");
        File.WriteAllText(orders, legacy);
        var stale = Path.Combine(SchemaRoot, VaultLayout.ReportsFolder, "DEMO — old report.md");
        File.WriteAllText(stale, "---\ndbviz_id: \"page:DEMO:report:old\"\n---\n# old\n");

        Render();

        var text = File.ReadAllText(orders);
        Assert.Contains(Note.ManualHeading + "\n" + Note.ManualPlaceholder + "\nkeep me", text);
        Assert.DoesNotContain(Note.LegacyManualHeadings[0], text);
        Assert.False(File.Exists(stale));
        Assert.True(File.Exists(Path.Combine(SchemaRoot, VaultLayout.ArchiveFolder, "DEMO — old report.md")));
        Assert.True(File.Exists(Path.Combine(SchemaRoot, "DEMO.md"))); // the MOC is never archived
    }
}
