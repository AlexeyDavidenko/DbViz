using System.Text;

namespace DbViz.Obsidian;

public enum WriteResult { Created, Updated, Unchanged, Moved }

/// <summary>
/// Writes files only inside the schema section, only when the content changed
/// (so obsidian-git does not commit no-op changes), and never deletes anything: vanished objects move to _archive.
/// </summary>
public sealed class VaultWriter
{
    private readonly string _root;
    private readonly Dictionary<string, string> _existingById = new(StringComparer.Ordinal);
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public VaultWriter(string schemaRoot)
    {
        _root = Path.GetFullPath(schemaRoot);
        Directory.CreateDirectory(_root);
        foreach (var file in Directory.EnumerateFiles(_root, "*.md", SearchOption.AllDirectories))
        {
            var rel = Rel(file);
            if (rel.StartsWith(VaultLayout.ArchiveFolder + "/", StringComparison.Ordinal)) continue;
            var id = NoteMerger.ReadId(ReadHead(file));
            if (id is not null) _existingById.TryAdd(id, rel);
        }
    }

    public string Root => _root;

    public WriteResult Write(Note note)
    {
        var moved = false;
        if (_existingById.TryGetValue(note.Id, out var oldRel) && oldRel != note.RelativePath)
        {
            var from = Full(oldRel);
            var to = Full(note.RelativePath);
            if (File.Exists(from) && !File.Exists(to))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Move(from, to);
                moved = true;
            }
        }
        _existingById[note.Id] = note.RelativePath;

        var path = Full(note.RelativePath);
        var existing = File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;
        var text = NoteMerger.Merge(note, existing);
        if (existing is not null && existing.Replace("\r\n", "\n") == text) return moved ? WriteResult.Moved : WriteResult.Unchanged;

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, Utf8NoBom);
        return existing is null ? WriteResult.Created : WriteResult.Updated;
    }

    /// <summary>Notes of objects that no longer exist in the DB are moved to _archive (not deleted).</summary>
    public List<string> ArchiveMissing(IReadOnlySet<string> currentIds, string objectIdPrefix)
    {
        var archived = new List<string>();
        foreach (var (id, rel) in _existingById.ToList())
        {
            if (!id.StartsWith(objectIdPrefix, StringComparison.Ordinal) || currentIds.Contains(id)) continue;
            var from = Full(rel);
            if (!File.Exists(from)) continue;
            var target = Full($"{VaultLayout.ArchiveFolder}/{Path.GetFileName(rel)}");
            if (File.Exists(target))
                target = Full($"{VaultLayout.ArchiveFolder}/{Path.GetFileNameWithoutExtension(rel)} ({DateTime.Now:yyyyMMdd-HHmmss}).md");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Move(from, target);
            _existingById.Remove(id);
            archived.Add(rel);
        }
        return archived;
    }

    private string Full(string rel)
    {
        var full = Path.GetFullPath(Path.Combine(_root, rel));
        if (!full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException($"Refusing to write outside the schema section: {rel}");
        return full;
    }

    private string Rel(string full) => Path.GetRelativePath(_root, full).Replace('\\', '/');

    private static string ReadHead(string file)
    {
        using var r = new StreamReader(file, Encoding.UTF8);
        var buf = new char[4096];
        var n = r.ReadBlock(buf, 0, buf.Length);
        return new string(buf, 0, n);
    }
}
