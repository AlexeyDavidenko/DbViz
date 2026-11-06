using System.Text;

namespace DbViz.Obsidian;

/// <summary>Small Markdown/Obsidian helpers.</summary>
public static class Md
{
    /// <summary>Table cell: "|" is escaped (also inside [[link|alias]], which Obsidian understands), line breaks become &lt;br&gt;.</summary>
    public static string Cell(string? s) =>
        string.IsNullOrEmpty(s) ? "" : s.Replace("\r", "").Replace("|", "\\|").Replace("\n", "<br>");

    public static string Table(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string?>> rows)
    {
        var sb = new StringBuilder();
        sb.Append("| ").Append(string.Join(" | ", headers)).Append(" |\n");
        sb.Append('|').Append(string.Concat(headers.Select(_ => "---|"))).Append('\n');
        var any = false;
        foreach (var r in rows)
        {
            any = true;
            sb.Append("| ").Append(string.Join(" | ", r.Select(Cell))).Append(" |\n");
        }
        return any ? sb.ToString().TrimEnd() : "";
    }

    public static string Code(string? s) => string.IsNullOrEmpty(s) ? "" : "`" + s.Replace("`", "'") + "`";

    public static string Truncate(string? s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var one = s.Replace("\r", "").Replace("\n", " ").Trim();
        return one.Length <= max ? one : one[..(max - 1)].TrimEnd() + "…";
    }

    /// <summary>Collapsed callout containing code. Long code is truncated to <paramref name="maxLines"/>.</summary>
    public static string CodeCallout(string title, string lang, string? code, int maxLines, bool collapsed = true)
    {
        if (string.IsNullOrWhiteSpace(code)) return "";
        var lines = code.Replace("\r", "").TrimEnd().Split('\n');
        var truncated = lines.Length > maxLines;
        if (truncated) lines = lines[..maxLines];
        var fence = code.Contains("```") ? "~~~~" : "```";
        var sb = new StringBuilder();
        sb.Append("> [!abstract]").Append(collapsed ? "- " : " ").Append(title).Append('\n');
        sb.Append("> ").Append(fence).Append(lang).Append('\n');
        foreach (var l in lines) sb.Append("> ").Append(l.TrimEnd()).Append('\n');
        sb.Append("> ").Append(fence).Append('\n');
        if (truncated) sb.Append($"> _…truncated: showing the first {maxLines} lines_\n");
        return sb.ToString().TrimEnd();
    }

    public static string Bullets(IEnumerable<string> items) => string.Join("\n", items.Select(i => "- " + i));

    /// <summary>Safe identifier for Mermaid.</summary>
    public static string MermaidId(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s) sb.Append(char.IsAsciiLetterOrDigit(c) || c == '_' ? c : '_');
        return sb.Length > 0 && char.IsAsciiDigit(sb[0]) ? "n" + sb : sb.ToString();
    }
}
