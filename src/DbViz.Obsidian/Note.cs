using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DbViz.Obsidian;

/// <summary>Generated note: frontmatter + title + named AUTO blocks.</summary>
public sealed class Note
{
    public required string Id { get; init; }
    /// <summary>Path relative to the schema section root, using "/".</summary>
    public required string RelativePath { get; set; }
    public required string Title { get; init; }
    public List<KeyValuePair<string, object?>> Frontmatter { get; } = [];
    public List<KeyValuePair<string, string>> Blocks { get; } = [];
    /// <summary>Whether a new note gets the manual "notes" section for hand-written text.</summary>
    public bool ManualSection { get; init; } = true;

    public Note Fm(string key, object? value)
    {
        if (value is null || value is string { Length: 0 }) return this;
        Frontmatter.Add(new(key, value));
        return this;
    }

    public Note Block(string name, string? content)
    {
        Blocks.Add(new(name, (content ?? "").Trim()));
        return this;
    }

    public const string ManualHeading = "## Notes";
    public const string ManualPlaceholder = "%% Manual section: the generator never touches it. %%";

    /// <summary>Manual-section headings/placeholders written by earlier (Russian) versions — still recognised.</summary>
    public static readonly string[] LegacyManualHeadings = ["## Заметки"];
    public static readonly string[] LegacyManualPlaceholders = ["%% Ручной раздел: генератор его не трогает. %%"];

    public string RenderNew()
    {
        var sb = new StringBuilder();
        sb.Append(RenderFrontmatter(null));
        sb.Append("# ").Append(Title).Append("\n\n");
        var first = true;
        foreach (var (name, content) in Blocks)
        {
            sb.Append(RenderBlock(name, content)).Append("\n\n");
            if (first && ManualSection)
            {
                sb.Append(ManualHeading).Append('\n').Append(ManualPlaceholder).Append("\n\n");
            }
            first = false;
        }
        return sb.ToString().TrimEnd() + "\n";
    }

    public static string RenderBlock(string name, string content) =>
        $"%% AUTO:BEGIN {name} %%\n{content}\n%% AUTO:END {name} %%";

    /// <summary>Renders the frontmatter; <paramref name="extraTags"/> are user tags that must be preserved.</summary>
    public string RenderFrontmatter(IEnumerable<string>? extraTags, IEnumerable<string>? preservedRaw = null)
    {
        var sb = new StringBuilder("---\n");
        foreach (var (key, value) in Frontmatter)
        {
            if (key == "tags" && value is IEnumerable<string> tags)
            {
                sb.Append("tags:\n");
                foreach (var t in tags.Concat(extraTags ?? []).Distinct()) sb.Append("  - ").Append(t).Append('\n');
                continue;
            }
            sb.Append(key).Append(": ").Append(Yaml(value)).Append('\n');
        }
        foreach (var raw in preservedRaw ?? []) sb.Append(raw.TrimEnd('\n')).Append('\n');
        sb.Append("---\n");
        return sb.ToString();
    }

    public static string Yaml(object? value) => value switch
    {
        null => "",
        bool b => b ? "true" : "false",
        int or long or decimal or double => Convert.ToString(value, CultureInfo.InvariantCulture)!,
        DateTime d => d.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
        IEnumerable<string> list => "[" + string.Join(", ", list.Select(QuoteYaml)) + "]",
        _ => QuoteYaml(Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""),
    };

    private static string QuoteYaml(string s) =>
        "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n") + "\"";
}

/// <summary>
/// Merges a new version of a note into the existing file: the generator owns only its frontmatter keys
/// and AUTO blocks; everything else (hand-written text, user properties, user tags) is preserved.
/// </summary>
public static class NoteMerger
{
    /// <summary>Frontmatter keys owned by the generator.</summary>
    public static readonly HashSet<string> GeneratedKeys =
    [
        "dbviz_id", "db_type", "layer", "schema", "name", "status", "created", "last_ddl_time", "num_rows", "last_analyzed",
        "partitioned", "temporary", "description_source", "tags", "table", "subprograms", "lines", "has_errors", "kind",
    ];

    /// <summary>Generator tag prefixes: such tags in the old file are replaced by the new ones.</summary>
    public static readonly string[] GeneratedTagPrefixes = ["db/", "layer/", "schema/", "status/", "dbviz"];

    private static readonly Regex TopLevelKey = new(@"^([A-Za-z0-9_\-]+):", RegexOptions.Compiled);

    public static string Merge(Note note, string? existing)
    {
        if (string.IsNullOrEmpty(existing)) return note.RenderNew();

        var text = existing.Replace("\r\n", "\n");
        var (fmEntries, body) = SplitFrontmatter(text);
        body = MigrateLegacyManualSection(body);

        var userTags = new List<string>();
        var preserved = new List<string>();
        foreach (var (key, raw) in fmEntries)
        {
            if (key == "tags")
            {
                userTags.AddRange(ParseTags(raw).Where(t => !GeneratedTagPrefixes.Any(p => t.StartsWith(p, StringComparison.Ordinal))));
                continue;
            }
            if (!GeneratedKeys.Contains(key)) preserved.Add(raw);
        }

        var newBody = body;
        foreach (var (name, content) in note.Blocks)
        {
            var re = BlockRegex(name);
            var block = Note.RenderBlock(name, content);
            newBody = re.IsMatch(newBody)
                ? re.Replace(newBody, _ => block, 1)
                : newBody.TrimEnd() + "\n\n" + block + "\n";
        }

        // blocks the generator no longer produces are emptied (markers stay: neighbouring edits may live there)
        var known = note.Blocks.Select(b => b.Key).ToHashSet();
        newBody = AnyBlock.Replace(newBody, m => known.Contains(m.Groups[1].Value) ? m.Value : Note.RenderBlock(m.Groups[1].Value, ""));

        return note.RenderFrontmatter(userTags, preserved) + newBody.TrimStart('\n').TrimEnd() + "\n";
    }

    /// <summary>Renames the manual-section heading/placeholder written by earlier versions; the user's text below stays as is.</summary>
    private static string MigrateLegacyManualSection(string body)
    {
        foreach (var p in Note.LegacyManualPlaceholders) body = body.Replace(p, Note.ManualPlaceholder);
        foreach (var h in Note.LegacyManualHeadings)
            body = Regex.Replace(body, $"^{Regex.Escape(h)}$", Note.ManualHeading, RegexOptions.Multiline);
        return body;
    }

    private static Regex BlockRegex(string name) =>
        new($@"%% AUTO:BEGIN {Regex.Escape(name)} %%.*?%% AUTO:END {Regex.Escape(name)} %%", RegexOptions.Singleline);

    private static readonly Regex AnyBlock = new(@"%% AUTO:BEGIN ([\w\-]+) %%.*?%% AUTO:END \1 %%", RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>Returns dbviz_id from the frontmatter, or null.</summary>
    public static string? ReadId(string text)
    {
        var (entries, _) = SplitFrontmatter(text.Replace("\r\n", "\n"));
        var raw = entries.FirstOrDefault(e => e.Key == "dbviz_id").Raw;
        if (raw is null) return null;
        var v = raw[(raw.IndexOf(':') + 1)..].Trim();
        return v.Trim('"', '\'');
    }

    private static (List<(string Key, string Raw)> Entries, string Body) SplitFrontmatter(string text)
    {
        var entries = new List<(string, string)>();
        if (!text.StartsWith("---\n", StringComparison.Ordinal)) return (entries, text);
        var end = text.IndexOf("\n---", 3, StringComparison.Ordinal);
        if (end < 0) return (entries, text);
        var fm = text[4..(end + 1)];
        var afterIdx = text.IndexOf('\n', end + 1);
        var body = afterIdx < 0 ? "" : text[(afterIdx + 1)..];

        string? key = null;
        var sb = new StringBuilder();
        foreach (var line in fm.Split('\n'))
        {
            var m = TopLevelKey.Match(line);
            if (m.Success)
            {
                if (key is not null) entries.Add((key, sb.ToString()));
                key = m.Groups[1].Value;
                sb.Clear().Append(line).Append('\n');
            }
            else if (key is not null && line.Length > 0) sb.Append(line).Append('\n');
        }
        if (key is not null) entries.Add((key, sb.ToString()));
        return (entries, body);
    }

    private static IEnumerable<string> ParseTags(string raw)
    {
        var value = raw[(raw.IndexOf(':') + 1)..];
        var parts = value.Contains('[')
            ? value.Trim().Trim('[', ']').Split(',')
            : value.Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith('-')).Select(l => l[1..]);
        return parts.Select(p => p.Trim().Trim('"', '\'').TrimStart('#')).Where(p => p.Length > 0);
    }
}
