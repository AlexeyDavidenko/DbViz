using DbViz.Oracle;

namespace DbViz.Tests;

public class ReadOnlyGuardTests
{
    public static TheoryData<string> AllQueries()
    {
        var data = new TheoryData<string>();
        foreach (var q in DictionaryQueries.All) data.Add(q.Name);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllQueries))]
    public void Every_dictionary_query_passes_guard(string name)
    {
        var q = DictionaryQueries.All.Single(x => x.Name == name);
        ReadOnlyGuard.EnsureSelectOnly(q.Sql);
    }

    [Fact]
    public void Every_dictionary_query_filters_by_owner_or_is_session_info()
    {
        foreach (var q in DictionaryQueries.All.Where(q => q.Name is not ("current_user" or "version")))
            Assert.Contains(":owner", q.Sql);
    }

    [Theory]
    [InlineData("DELETE FROM orders")]
    [InlineData("UPDATE orders SET status = 'X'")]
    [InlineData("INSERT INTO t VALUES (1)")]
    [InlineData("MERGE INTO t USING s ON (1=1) WHEN MATCHED THEN UPDATE SET a = 1")]
    [InlineData("CREATE TABLE x (a NUMBER)")]
    [InlineData("DROP TABLE orders")]
    [InlineData("TRUNCATE TABLE orders")]
    [InlineData("ALTER SESSION SET nls_date_format = 'YYYY'")]
    [InlineData("BEGIN NULL; END;")]
    [InlineData("DECLARE x NUMBER; BEGIN NULL; END;")]
    [InlineData("GRANT SELECT ON t TO u")]
    [InlineData("COMMENT ON TABLE t IS 'x'")]
    [InlineData("SELECT 1 FROM dual; DELETE FROM t")]
    [InlineData("SELECT * FROM orders FOR UPDATE")]
    [InlineData("SELECT * FROM orders@remote")]
    [InlineData("WITH FUNCTION f RETURN NUMBER IS BEGIN RETURN 1; END; SELECT f FROM dual")]
    [InlineData("SELECT * FROM t /* comment */ ; DROP TABLE t")]
    [InlineData("  call dbms_stats.gather_schema_stats('HR')")]
    public void Rejects_anything_but_plain_select(string sql)
    {
        Assert.Throws<ReadOnlyViolationException>(() => ReadOnlyGuard.EnsureSelectOnly(sql));
    }

    [Theory]
    [InlineData("SELECT object_type FROM all_objects WHERE object_type IN ('PACKAGE BODY','PROCEDURE','FUNCTION')")]
    [InlineData("SELECT delete_rule, comments, dropped FROM all_constraints")]
    [InlineData("select 'DROP TABLE x; DELETE' as s from dual")]
    [InlineData("-- DELETE in comment\nSELECT 1 FROM dual")]
    public void Literals_comments_and_column_names_do_not_trigger_false_positives(string sql)
    {
        ReadOnlyGuard.EnsureSelectOnly(sql);
    }
}
