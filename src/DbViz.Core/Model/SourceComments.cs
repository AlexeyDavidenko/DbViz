using System.Text.RegularExpressions;

namespace DbViz.Core.Model;

/// <summary>Extracts descriptions from comments in PL/SQL source (heuristic).</summary>
public static class SourceComments
{
    /// <summary>
    /// First comment right after the object header (PACKAGE x AS / PROCEDURE x(...) IS):
    /// a <c>/* ... */</c> block or consecutive <c>--</c> lines.
    /// </summary>
    public static string? HeaderComment(string? source, int scanLines = 40)
    {
        if (string.IsNullOrWhiteSpace(source)) return null;
        var lines = source.Split('\n').Take(scanLines).ToArray();
        for (var i = 0; i < lines.Length; i++)
        {
            var t = lines[i].Trim();
            if (t.StartsWith("/*", StringComparison.Ordinal)) return ReadBlock(lines, i);
            if (t.StartsWith("--", StringComparison.Ordinal)) return ReadDashes(lines, i);
            // the first "real" line after the header is not a comment: stop searching
            if (i > 0 && t.Length > 0 && !IsHeaderLine(t)) return null;
        }
        return null;
    }

    /// <summary>Comment directly above a subprogram declaration in a package spec.</summary>
    public static string? CommentBefore(string? source, string subprogramName)
    {
        if (string.IsNullOrWhiteSpace(source)) return null;
        var lines = source.Split('\n');
        var decl = new Regex($@"^\s*(PROCEDURE|FUNCTION)\s+""?{Regex.Escape(subprogramName)}""?\b", RegexOptions.IgnoreCase);
        for (var i = 0; i < lines.Length; i++)
        {
            if (!decl.IsMatch(lines[i])) continue;
            // a comment after the declaration on the same line: PROCEDURE X; -- description
            var j = i - 1;
            while (j >= 0 && lines[j].Trim().Length == 0) j--;
            if (j < 0) return null;
            var prev = lines[j].Trim();
            if (prev.StartsWith("--", StringComparison.Ordinal))
            {
                var start = j;
                while (start > 0 && lines[start - 1].Trim().StartsWith("--", StringComparison.Ordinal)) start--;
                return ReadDashes(lines, start);
            }
            if (prev.EndsWith("*/", StringComparison.Ordinal))
            {
                var start = j;
                while (start > 0 && !lines[start].Contains("/*")) start--;
                return ReadBlock(lines, start);
            }
            return null;
        }
        return null;
    }

    private static bool IsHeaderLine(string t) =>
        Regex.IsMatch(t, @"^(CREATE|PACKAGE|PROCEDURE|FUNCTION|TYPE|TRIGGER|AUTHID|AS|IS|\(|\)|RETURN|[A-Za-z0-9_""$#]+\s+(IN|OUT)\b)", RegexOptions.IgnoreCase)
        || t.StartsWith(",") || t.EndsWith(",") || t.EndsWith(" IS", StringComparison.OrdinalIgnoreCase) || t.EndsWith(" AS", StringComparison.OrdinalIgnoreCase);

    private static string? ReadBlock(string[] lines, int start)
    {
        var parts = new List<string>();
        for (var i = start; i < lines.Length; i++)
        {
            var l = lines[i];
            var endIdx = l.IndexOf("*/", StringComparison.Ordinal);
            var piece = endIdx >= 0 ? l[..endIdx] : l;
            piece = piece.Replace("/*", "").Trim().TrimStart('*').Trim();
            if (piece.Length > 0) parts.Add(piece);
            if (endIdx >= 0) break;
        }
        return Clean(parts);
    }

    private static string? ReadDashes(string[] lines, int start)
    {
        var parts = new List<string>();
        for (var i = start; i < lines.Length; i++)
        {
            var t = lines[i].Trim();
            if (!t.StartsWith("--", StringComparison.Ordinal)) break;
            parts.Add(t.TrimStart('-').Trim());
        }
        return Clean(parts);
    }

    private static string? Clean(List<string> parts)
    {
        // drop "frames" like ---------- and ========
        var text = string.Join(" ", parts.Where(p => p.Length > 0 && !Regex.IsMatch(p, @"^[-=*#_~\s]+$"))).Trim();
        return text.Length >= 3 ? text : null;
    }
}
