using DbViz.Obsidian;

namespace DbViz.Tests;

public class NoteMergerTests
{
    private static Note MakeNote(string description, string columns, bool withExtra = true)
    {
        var n = new Note { Id = "HR.T", RelativePath = "10 Tables/HR.T.md", Title = "T" };
        n.Fm("dbviz_id", "HR.T").Fm("status", "VALID").Fm("tags", new List<string> { "db/table", "layer/storage" });
        n.Block("description", description).Block("columns", columns);
        if (withExtra) n.Block("extra", "extra content");
        return n;
    }

    [Fact]
    public void New_note_has_blocks_and_manual_section()
    {
        var text = NoteMerger.Merge(MakeNote("desc", "cols"), null);
        Assert.StartsWith("---\ndbviz_id: \"HR.T\"", text);
        Assert.Contains("%% AUTO:BEGIN description %%\ndesc\n%% AUTO:END description %%", text);
        Assert.Contains(Note.ManualHeading, text);
        Assert.True(text.IndexOf("AUTO:END description", StringComparison.Ordinal) < text.IndexOf(Note.ManualHeading, StringComparison.Ordinal));
    }

    [Fact]
    public void Manual_text_user_properties_and_user_tags_survive_regeneration()
    {
        var original = NoteMerger.Merge(MakeNote("old desc", "old cols"), null);
        var edited = original
            .Replace("status: \"VALID\"", "status: \"VALID\"\nowner_team: \"Billing\"\nrelated:\n  - \"[[Some note]]\"")
            .Replace("  - layer/storage", "  - layer/storage\n  - important")
            .Replace(Note.ManualPlaceholder, "My manual text with $1 and \\d is not a regex.");

        var merged = NoteMerger.Merge(MakeNote("new desc", "new cols"), edited);

        Assert.Contains("new desc", merged);
        Assert.Contains("new cols", merged);
        Assert.DoesNotContain("old desc", merged);
        Assert.Contains("My manual text with $1 and \\d is not a regex.", merged);
        Assert.Contains("owner_team: \"Billing\"", merged);
        Assert.Contains("  - \"[[Some note]]\"", merged);
        Assert.Contains("  - important", merged);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(merged, "db/table"));
    }

    [Fact]
    public void Merge_is_idempotent()
    {
        var first = NoteMerger.Merge(MakeNote("d", "c"), null);
        var second = NoteMerger.Merge(MakeNote("d", "c"), first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void Block_no_longer_produced_is_emptied_but_markers_kept()
    {
        var first = NoteMerger.Merge(MakeNote("d", "c"), null);
        var merged = NoteMerger.Merge(MakeNote("d", "c", withExtra: false), first);
        Assert.Contains("%% AUTO:BEGIN extra %%\n\n%% AUTO:END extra %%", merged);
        Assert.DoesNotContain("extra content", merged);
    }

    [Fact]
    public void New_block_is_appended_to_old_file()
    {
        var old = "---\ndbviz_id: \"HR.T\"\n---\n# T\n\nmanual text\n";
        var merged = NoteMerger.Merge(MakeNote("d", "c"), old);
        Assert.Contains("manual text", merged);
        Assert.Contains("%% AUTO:BEGIN columns %%", merged);
    }

    [Fact]
    public void Reads_id_from_frontmatter()
    {
        Assert.Equal("HR.T", NoteMerger.ReadId(NoteMerger.Merge(MakeNote("d", "c"), null)));
        Assert.Null(NoteMerger.ReadId("# just a note"));
    }
}
