using DbViz.Core;
using DbViz.Core.Model;

namespace DbViz.Obsidian;

public sealed record RenderSummary(int Created, int Updated, int Unchanged, int Moved, IReadOnlyList<string> Archived, string SchemaRoot);

public static class ObsidianRenderer
{
    /// <param name="vaultRoot">Vault root (the folder containing .obsidian).</param>
    /// <param name="section">Section inside the vault, e.g. Databases.</param>
    public static RenderSummary Render(Snapshot snapshot, string vaultRoot, string section, RenderOptions? options, ILog log,
        IndexOptions? indexOptions = null)
    {
        var ix = new SchemaIndex(snapshot, indexOptions);
        var layout = new VaultLayout(ix);
        var renderer = new NoteRenderer(ix, layout, options ?? new RenderOptions());
        var sectionPath = $"{section.Trim('/')}/{ix.Schema}";
        var writer = new VaultWriter(Path.Combine(vaultRoot, sectionPath));
        var pages = new IndexPages(ix, layout, renderer);

        log.Info($"Rendering {ix.Objects.Count} objects, {ix.Edges.Count} relationships → {writer.Root}");
        int created = 0, updated = 0, unchanged = 0, moved = 0;
        void Count(WriteResult r)
        {
            switch (r)
            {
                case WriteResult.Created: created++; break;
                case WriteResult.Updated: updated++; break;
                case WriteResult.Moved: moved++; break;
                default: unchanged++; break;
            }
        }

        foreach (var o in ix.Objects.OrderBy(o => o.Id, StringComparer.Ordinal))
        {
            try { Count(writer.Write(renderer.Render(o))); }
            catch (Exception ex) { log.Error($"Could not render {o.Id} ({o.Kind})", ex); }
        }
        var pageIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in pages.All()) { pageIds.Add(p.Id); Count(writer.Write(p)); }

        // objects dropped from the DB, and service pages that are no longer produced (e.g. renamed indexes/reports)
        var archived = writer.ArchiveMissing(ix.Objects.Select(o => o.Id).ToHashSet(), ix.Schema + ".");
        archived.AddRange(writer.ArchiveMissing(pageIds, $"page:{ix.Schema}:"));
        foreach (var a in archived) log.Info($"  archived: {a}");

        log.Info($"Done: created {created}, updated {updated}, unchanged {unchanged}, moved {moved}, archived {archived.Count}");
        return new RenderSummary(created, updated, unchanged, moved, archived, writer.Root);
    }
}
