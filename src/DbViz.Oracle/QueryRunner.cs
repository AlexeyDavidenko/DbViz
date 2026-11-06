using System.Data;
using System.Diagnostics;
using System.Globalization;
using DbViz.Core;
using Oracle.ManagedDataAccess.Client;

namespace DbViz.Oracle;

public sealed record OracleConnectionSettings(string DataSource, string User, string Password, int CommandTimeoutSeconds = 600)
{
    /// <summary>Also accepts the JDBC format: jdbc:oracle:thin:@//host:1521/service.</summary>
    public static string NormalizeDataSource(string dataSource)
    {
        var ds = dataSource.Trim();
        const string jdbc = "jdbc:oracle:thin:@";
        return ds.StartsWith(jdbc, StringComparison.OrdinalIgnoreCase) ? ds[jdbc.Length..] : ds;
    }

    public string ConnectionString => new OracleConnectionStringBuilder
    {
        DataSource = NormalizeDataSource(DataSource),
        UserID = User,
        Password = Password,
        Pooling = false,
        ConnectionTimeout = 30,
    }.ConnectionString;
}

/// <summary>
/// Executes only queries from <see cref="DictionaryQueries"/>, inside a read-only transaction.
/// </summary>
public sealed class QueryRunner : IDisposable
{
    private readonly OracleConnection _connection;
    private readonly OracleTransaction _transaction;
    private readonly ILog _log;
    private readonly int _timeout;

    public bool ReadOnlyTransaction { get; }

    public QueryRunner(OracleConnectionSettings settings, ILog log)
    {
        _log = log;
        _timeout = settings.CommandTimeoutSeconds;
        _connection = new OracleConnection(settings.ConnectionString);
        _connection.Open();
        // visible to DBAs in V$SESSION: clear who is connected and why
        _connection.ModuleName = "DbViz";
        _connection.ActionName = "metadata-extract";
        _connection.ClientInfo = "read-only dictionary SELECT";

        _transaction = _connection.BeginTransaction();
        try
        {
            // Changes nothing in the DB: forbids DML in this transaction and gives a consistent dictionary snapshot.
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = _transaction;
            cmd.CommandText = "SET TRANSACTION READ ONLY";
            cmd.ExecuteNonQuery();
            ReadOnlyTransaction = true;
        }
        catch (OracleException ex)
        {
            _log.Warn($"SET TRANSACTION READ ONLY was not applied ({ex.Number}: {ex.Message.Trim()}). ReadOnlyGuard still protects every query.");
        }
    }

    public List<T> Run<T>(DictionaryQueries.Query query, string owner, Func<Row, T> map)
    {
        ReadOnlyGuard.EnsureSelectOnly(query.Sql);

        var sw = Stopwatch.StartNew();
        using var cmd = _connection.CreateCommand();
        cmd.Transaction = _transaction;
        cmd.CommandText = query.Sql;
        cmd.BindByName = true;
        cmd.InitialLONGFetchSize = -1; // LONG columns (view TEXT, DATA_DEFAULT, SEARCH_CONDITION) are read in full
        cmd.CommandTimeout = _timeout;
        if (query.Sql.Contains(":owner", StringComparison.Ordinal))
            cmd.Parameters.Add(new OracleParameter("owner", OracleDbType.Varchar2, owner, ParameterDirection.Input));

        var result = new List<T>();
        using (var reader = cmd.ExecuteReader())
        {
            reader.FetchSize = 4 * 1024 * 1024; // fewer round trips to a remote DB
            var row = new Row(reader);
            while (reader.Read()) result.Add(map(row));
        }
        _log.Info($"  {query.Name,-20} {result.Count,8} rows  {sw.Elapsed.TotalSeconds,7:F1} s");
        return result;
    }

    public void Dispose()
    {
        // End the read-only transaction; it has no changes and cannot have any.
        try { _transaction.Rollback(); } catch { /* connection already closed */ }
        _transaction.Dispose();
        _connection.Dispose();
    }
}

/// <summary>Convenient access to columns by name with cached ordinals.</summary>
public sealed class Row(IDataRecord record)
{
    private Dictionary<string, int>? _ordinals;

    private int Ord(string name)
    {
        if (_ordinals is null)
        {
            _ordinals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < record.FieldCount; i++) _ordinals[record.GetName(i)] = i;
        }
        return _ordinals.TryGetValue(name, out var o) ? o : throw new KeyNotFoundException($"Column {name} not found in the query result.");
    }

    public string? Str(string name)
    {
        var o = Ord(name);
        return record.IsDBNull(o) ? null : Convert.ToString(record.GetValue(o), CultureInfo.InvariantCulture);
    }

    public string ReqStr(string name) => Str(name) ?? "";

    public int? Int(string name)
    {
        var o = Ord(name);
        return record.IsDBNull(o) ? null : Convert.ToInt32(record.GetValue(o), CultureInfo.InvariantCulture);
    }

    public long? Long(string name)
    {
        var o = Ord(name);
        return record.IsDBNull(o) ? null : Convert.ToInt64(record.GetValue(o), CultureInfo.InvariantCulture);
    }

    public DateTime? Date(string name)
    {
        var o = Ord(name);
        return record.IsDBNull(o) ? null : Convert.ToDateTime(record.GetValue(o), CultureInfo.InvariantCulture);
    }

    /// <summary>Y / YES / TRUE / ENABLED → true.</summary>
    public bool Flag(string name) => Str(name)?.Trim().ToUpperInvariant() is "Y" or "YES" or "TRUE" or "ENABLED";
}
