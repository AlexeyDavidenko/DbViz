using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using DbViz.Core.Model;

namespace DbViz.Core;

public static class SnapshotSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // keep non-Latin comments readable
    };

    public static void Save(Snapshot snapshot, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var tmp = path + ".tmp";
        using (var fs = File.Create(tmp))
            JsonSerializer.Serialize(fs, snapshot, Options);
        File.Move(tmp, path, overwrite: true);
    }

    public static Snapshot Load(string path)
    {
        using var fs = File.OpenRead(path);
        var s = JsonSerializer.Deserialize<Snapshot>(fs, Options) ?? throw new InvalidDataException($"Empty snapshot: {path}");
        if (s.FormatVersion > Snapshot.CurrentFormatVersion)
            throw new InvalidDataException($"Snapshot format v{s.FormatVersion} is newer than this tool supports (v{Snapshot.CurrentFormatVersion}).");
        return s;
    }

    public static string ToJson(Snapshot snapshot) => JsonSerializer.Serialize(snapshot, Options);
    public static Snapshot FromJson(string json) => JsonSerializer.Deserialize<Snapshot>(json, Options)!;
}
