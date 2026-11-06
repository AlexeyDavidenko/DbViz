using DbViz.Cli;
using DbViz.Core;
using DbViz.Core.Demo;
using DbViz.Core.Model;
using DbViz.Obsidian;
using DbViz.Oracle;

const string Usage = """
    DbViz - Oracle schema documentation in Obsidian (read-only access to the data dictionary).

    Commands:
      check                 test the connection, show object counts in the schema
      extract               extract the schema dictionary into a snapshot (JSON)
      render                generate Obsidian notes from the snapshot
      sync                  extract + render
      stats                 snapshot summary (no DB connection)
      queries               print every SQL statement the tool runs against the DB
      graph-config          write layer colors and graph presets (bookmarks) into the vault .obsidian
      export                build a standalone vault + zip for other people (from the snapshot, with manual edits)
      demo                  render a vault for the fictional DEMO schema (no DB)

    Options:
      --config <path>       path to config/dbviz.json (default: searched upwards from the current folder)
      --snapshot <path>     snapshot path (default: from the config)
      --vault <path>        Obsidian vault root (default: from the config)
      --out <path>          for demo/export: output folder (default: out/demo-vault / out/export)
    """;

if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    Console.WriteLine(Usage);
    return 0;
}

var command = args[0].ToLowerInvariant();
string? Opt(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

if (command == "queries")
{
    foreach (var q in DictionaryQueries.All)
    {
        ReadOnlyGuard.EnsureSelectOnly(q.Sql);
        Console.WriteLine($"-- {q.Name}\n{q.Sql.Trim()};\n");
    }
    return 0;
}

var cfg = AppConfig.Load(Opt("--config"));

using var log = new FileLog(cfg.Resolve(cfg.LogDir), command);
try
{
    var snapshotPath = cfg.Resolve(Opt("--snapshot") ?? cfg.SnapshotPath!);
    var vaultRoot = Opt("--vault") ?? cfg.Vault.Root;
    var indexOptions = BuildIndexOptions(cfg);
    var renderOptions = new RenderOptions { MaxSourceLines = cfg.MaxSourceLines };

    switch (command)
    {
        case "check":
            Extractor(cfg, log).Check(cfg.Schema);
            break;

        case "extract":
            DoExtract();
            break;

        case "render":
            DoRender(SnapshotSerializer.Load(snapshotPath));
            break;

        case "sync":
            DoRender(DoExtract());
            break;

        case "graph-config":
            ObsidianConfigWriter.Apply(vaultRoot, cfg.Schema, $"{cfg.Vault.Section.Trim('/')}/{cfg.Schema}", log);
            log.Info("To make Obsidian pick up the settings: Cmd/Ctrl+P → \"Reload app without saving\".");
            break;

        case "export":
            var snap = SnapshotSerializer.Load(snapshotPath);
            StandaloneExporter.Export(snap, Path.Combine(vaultRoot, cfg.Vault.Section, snap.Schema),
                cfg.Resolve(Opt("--out") ?? "out/export"), renderOptions, log, indexOptions);
            break;

        case "stats":
            PrintStats(SnapshotSerializer.Load(snapshotPath), indexOptions, log);
            break;

        case "demo":
            var outDir = cfg.Resolve(Opt("--out") ?? "out/demo-vault");
            Directory.CreateDirectory(outDir);
            var demo = DemoSnapshot.Create();
            ObsidianRenderer.Render(demo, outDir, "DBVisualization", renderOptions, log);
            log.Info($"Open the folder {outDir} as a vault in Obsidian.");
            break;

        default:
            Console.Error.WriteLine($"Unknown command: {command}\n\n{Usage}");
            return 2;
    }

    Snapshot DoExtract()
    {
        var snapshot = Extractor(cfg, log).Extract(cfg.Schema);
        SnapshotSerializer.Save(snapshot, snapshotPath);
        log.Info($"Snapshot: {snapshotPath} ({new FileInfo(snapshotPath).Length / 1024.0 / 1024.0:F1} MB)");
        PrintStats(snapshot, indexOptions, log);
        return snapshot;
    }

    void DoRender(Snapshot snapshot)
    {
        if (!Directory.Exists(Path.Combine(vaultRoot, ".obsidian")))
            log.Warn($"{vaultRoot} has no .obsidian folder; is it really an Obsidian vault?");
        ObsidianRenderer.Render(snapshot, vaultRoot, cfg.Vault.Section, renderOptions, log, indexOptions);
    }

    if (log.FilePath is not null) log.Info($"Log: {log.FilePath}");
    return 0;
}
catch (Exception ex)
{
    log.Error("Command failed", ex);
    if (log.FilePath is not null) Console.Error.WriteLine($"Details in the log: {log.FilePath}");
    return 1;
}

static OracleExtractor Extractor(AppConfig cfg, ILog log)
{
    var (dataSource, user, password) = cfg.LoadCredentials();
    return new OracleExtractor(new OracleConnectionSettings(dataSource, user, password, cfg.CommandTimeoutSeconds), log);
}

static IndexOptions BuildIndexOptions(AppConfig cfg)
{
    var o = new IndexOptions();
    return new IndexOptions
    {
        ExcludeNamePatterns = cfg.ExcludeNamePatterns ?? o.ExcludeNamePatterns,
        ExcludedRefOwners = cfg.ExcludedRefOwners is null ? o.ExcludedRefOwners : new HashSet<string>(cfg.ExcludedRefOwners, StringComparer.OrdinalIgnoreCase),
    };
}

static void PrintStats(Snapshot s, IndexOptions options, ILog log)
{
    var ix = new SchemaIndex(s, options);
    log.Info($"Schema {s.Schema}, extracted {s.ExtractedAt:yyyy-MM-dd HH:mm}, {s.DbVersion}");
    foreach (var g in ix.Objects.GroupBy(o => o.Kind).OrderBy(g => g.Key))
        log.Info($"  {KindInfo.Plural(g.Key),-34} {g.Count(),6}   INVALID: {g.Count(o => !o.IsValid)}");
    log.Info($"  Relationships: {ix.Edges.Count} (FK: {ix.Edges.Count(e => e.Kind == EdgeKind.Fk)}, " +
             $"dependencies: {ix.Edges.Count(e => e.Kind == EdgeKind.Depends)}, external: {ix.Edges.Count(e => e.ToExternal is not null)})");
    var tables = ix.OfKind(ObjectKind.Table).ToList();
    if (tables.Count > 0)
        log.Info($"  Tables with a comment: {tables.Count(t => ix.CommentOf(t.Name) is not null)} of {tables.Count}");
    log.Info($"  Source lines: {s.Sources.Sum(x => x.Lines)}; package bodies: {s.Sources.Count(x => x.Type == "PACKAGE BODY")} " +
             $"of {ix.OfKind(ObjectKind.Package).Count()} packages");
    foreach (var w in s.Warnings) log.Warn($"  no data [{w.Query}]: {w.Message}");
}
