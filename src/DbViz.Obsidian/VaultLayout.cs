using DbViz.Core.Model;

namespace DbViz.Obsidian;

/// <summary>Folders, file names and links.</summary>
public sealed class VaultLayout
{
    public const string IndexFolder = "_index";
    public const string ReportsFolder = "_reports";
    public const string ArchiveFolder = "_archive";

    private readonly Dictionary<string, string> _fileNames = new(StringComparer.Ordinal);

    public string Schema { get; }

    public VaultLayout(SchemaIndex index)
    {
        Schema = index.Schema;
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // macOS: the file system is case-insensitive
        foreach (var o in index.Objects.OrderBy(o => o.Id, StringComparer.Ordinal))
        {
            var name = Sanitize(o.Id);
            var candidate = name;
            for (var i = 2; !used.Add(candidate); i++) candidate = $"{name}_{i}";
            _fileNames[o.Id] = candidate;
        }
    }

    public static string Folder(ObjectKind kind) => kind switch
    {
        ObjectKind.Table => "10 Tables",
        ObjectKind.Sequence => "11 Sequences",
        ObjectKind.View => "20 Views",
        ObjectKind.MaterializedView => "21 Materialized Views",
        ObjectKind.Synonym => "22 Synonyms",
        ObjectKind.Package => "30 Packages",
        ObjectKind.Procedure => "31 Procedures",
        ObjectKind.Function => "32 Functions",
        ObjectKind.Trigger => "33 Triggers",
        ObjectKind.Type => "40 Types",
        ObjectKind.DbLink => "41 DB Links",
        ObjectKind.Job => "42 Jobs",
        _ => "99 Other",
    };

    public string FileName(DbObject o) => _fileNames[o.Id];

    public string RelativePath(DbObject o) => $"{Folder(o.Kind)}/{FileName(o)}.md";

    public string MocName => Schema;
    public static string IndexPageName(ObjectKind kind) => KindInfo.Plural(kind);

    /// <summary>[[HR.ORDERS|ORDERS]] or [[HR.PKG#PROC|PKG.PROC]].</summary>
    public string Link(DbObject o, string? heading = null, string? alias = null)
    {
        var target = FileName(o) + (heading is null ? "" : "#" + heading);
        var text = alias ?? (heading is null ? o.Name : $"{o.Name}.{heading}");
        return $"[[{target}|{text}]]";
    }

    /// <summary>Characters not allowed in Obsidian file names/links are replaced with "_".</summary>
    public static string Sanitize(string s)
    {
        var chars = s.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
            if ("\\/:*?\"<>|#^[]".Contains(chars[i]) || char.IsControl(chars[i])) chars[i] = '_';
        return new string(chars).Trim().TrimEnd('.');
    }
}
