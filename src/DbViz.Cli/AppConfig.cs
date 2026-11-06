using System.Text.Json;

namespace DbViz.Cli;

public sealed class AppConfig
{
    public string Schema { get; set; } = "";
    public string DataSource { get; set; } = "";
    /// <summary>Defaults to out/snapshot/&lt;schema&gt;.json.</summary>
    public string? SnapshotPath { get; set; }
    public string LogDir { get; set; } = "out/logs";
    public int CommandTimeoutSeconds { get; set; } = 600;
    public VaultConfig Vault { get; set; } = new();
    public int MaxSourceLines { get; set; } = 3000;
    public List<string>? ExcludeNamePatterns { get; set; }
    public List<string>? ExcludedRefOwners { get; set; }

    /// <summary>Project directory (the one containing the config folder); relative paths are resolved against it.</summary>
    [System.Text.Json.Serialization.JsonIgnore] public string BaseDir { get; private set; } = "";

    public string Resolve(string path) =>
        Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(BaseDir, ExpandHome(path)));

    private static string ExpandHome(string p) =>
        p.StartsWith("~/", StringComparison.Ordinal) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), p[2..]) : p;

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip };

    public static AppConfig Load(string? explicitPath)
    {
        var path = explicitPath ?? Find("config/dbviz.json")
                   ?? throw new FileNotFoundException(Find("config/dbviz.example.json") is { } example
                       ? $"config/dbviz.json not found. Copy {example} to config/dbviz.json and set the schema and vault."
                       : "config/dbviz.json not found (searched in the current folder and its parents). Use --config <path>.");
        var cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path), Json) ?? new AppConfig();
        if (string.IsNullOrWhiteSpace(cfg.Schema)) throw new InvalidDataException($"No schema set in {path}.");
        cfg.Schema = cfg.Schema.Trim().ToUpperInvariant();
        cfg.SnapshotPath ??= $"out/snapshot/{cfg.Schema}.json";
        cfg.BaseDir = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(path)))!;
        cfg.Vault.Root = ExpandHome(cfg.Vault.Root);
        return cfg;
    }

    /// <summary>
    /// DB address and credentials come only from a file outside git (config/credentials.json) or from the environment variables
    /// DBVIZ_DATASOURCE / DBVIZ_USER / DBVIZ_PASSWORD (which take precedence). dataSource in dbviz.json is a legacy fallback.
    /// </summary>
    public (string DataSource, string User, string Password) LoadCredentials()
    {
        var file = Path.Combine(BaseDir, "config", "credentials.json");
        Credentials c = new();
        if (File.Exists(file))
            c = JsonSerializer.Deserialize<Credentials>(File.ReadAllText(file), Json) ?? new Credentials();

        var ds = Env("DBVIZ_DATASOURCE") ?? NullIfEmpty(c.DataSource) ?? NullIfEmpty(DataSource);
        var user = Env("DBVIZ_USER") ?? NullIfEmpty(c.User);
        var pass = Env("DBVIZ_PASSWORD") ?? NullIfEmpty(c.Password);

        if (ds is null || user is null || pass is null)
        {
            var missing = string.Join(", ", new[] { ds is null ? "dataSource" : null, user is null ? "user" : null, pass is null ? "password" : null }.OfType<string>());
            throw new InvalidDataException(
                $"Missing: {missing}. Fill in {file} (see credentials.example.json) " +
                "or set DBVIZ_DATASOURCE / DBVIZ_USER / DBVIZ_PASSWORD.");
        }
        return (ds, user, pass);
    }

    private static string? Env(string name) => NullIfEmpty(Environment.GetEnvironmentVariable(name));
    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static string? Find(string relative)
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, relative);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

    private sealed class Credentials
    {
        public string? DataSource { get; set; }
        public string? User { get; set; }
        public string? Password { get; set; }
    }
}

public sealed class VaultConfig
{
    public string Root { get; set; } = "~/ObsidianVault";
    public string Section { get; set; } = "Databases";
}
