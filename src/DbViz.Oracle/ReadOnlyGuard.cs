using System.Text.RegularExpressions;

namespace DbViz.Oracle;

/// <summary>
/// Last line of defence before SQL reaches the database: lets through only a single SELECT/WITH
/// without DML/DDL/PL-SQL. String literals and comments are stripped before the check,
/// so values like 'PACKAGE BODY' cause no false positives and a command cannot be hidden in a literal.
/// </summary>
public static class ReadOnlyGuard
{
    private static readonly Regex Forbidden = new(
        @"\b(INSERT|UPDATE|DELETE|MERGE|UPSERT|CREATE|ALTER|DROP|TRUNCATE|RENAME|GRANT|REVOKE|COMMENT|PURGE|FLASHBACK|ANALYZE|AUDIT|NOAUDIT|" +
        @"BEGIN|DECLARE|CALL|EXEC|EXECUTE|COMMIT|ROLLBACK|SAVEPOINT|LOCK|FUNCTION|PROCEDURE|SHUTDOWN|STARTUP)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Literals = new(@"'(?:[^']|'')*'", RegexOptions.Compiled);
    private static readonly Regex LineComments = new(@"--[^\n]*", RegexOptions.Compiled);
    private static readonly Regex BlockComments = new(@"/\*.*?\*/", RegexOptions.Singleline | RegexOptions.Compiled);

    public static void EnsureSelectOnly(string sql)
    {
        var cleaned = BlockComments.Replace(sql, " ");
        cleaned = LineComments.Replace(cleaned, " ");
        cleaned = Literals.Replace(cleaned, "''").Trim();

        if (!Regex.IsMatch(cleaned, @"^(SELECT|WITH)\b", RegexOptions.IgnoreCase))
            throw new ReadOnlyViolationException("Only SELECT/WITH is allowed.", sql);
        if (cleaned.Contains(';'))
            throw new ReadOnlyViolationException("Multiple statements in one query are not allowed.", sql);
        if (cleaned.Contains('@'))
            throw new ReadOnlyViolationException("Access through a db link is not allowed.", sql);
        var m = Forbidden.Match(cleaned);
        if (m.Success)
            throw new ReadOnlyViolationException($"Forbidden keyword: {m.Value.ToUpperInvariant()}.", sql);
        if (Regex.IsMatch(cleaned, @"\bFOR\s+UPDATE\b", RegexOptions.IgnoreCase))
            throw new ReadOnlyViolationException("SELECT ... FOR UPDATE is not allowed.", sql);
    }
}

public sealed class ReadOnlyViolationException(string message, string sql)
    : InvalidOperationException($"ReadOnlyGuard: {message}\nSQL: {sql}");
