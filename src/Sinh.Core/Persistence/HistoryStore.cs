using System.Text.Json;

namespace Sinh.Core;

public sealed record HistoryEntry(Guid Id, Guid PairId, string OriginalPath, string ArchivePath, Entry Version, DateTimeOffset CreatedAt);

public sealed class HistoryStore(string dataDirectory)
{
    public string Root { get; } = Path.Combine(Path.GetFullPath(dataDirectory), "history");
    public async Task<HistoryEntry> ArchiveAsync(Guid pairId, string originalPath, Entry version, long limitBytes, CancellationToken token)
    {
        Directory.CreateDirectory(Root);
        long used = Directory.EnumerateFiles(Root, "*.data", SearchOption.AllDirectories).Sum(p => new FileInfo(p).Length);
        if (used + version.Length > limitBytes) throw new IOException("История заполнена. Освободите место в разделе восстановления; текущий файл не заменён.");
        var id = Guid.NewGuid(); var folder = Path.Combine(Root, pairId.ToString("N")); Directory.CreateDirectory(folder);
        var archive = Path.Combine(folder, id.ToString("N") + ".data");
        try
        {
            await VerifiedCopy.CopyAsync(originalPath, archive, version, 0, null, "История", token).ConfigureAwait(false);
            var entry = new HistoryEntry(id, pairId, originalPath, archive, version, DateTimeOffset.UtcNow);
            await StateStore.AtomicJsonAsync(Path.ChangeExtension(archive, ".json"), entry, token).ConfigureAwait(false);
            return entry;
        }
        catch { if (File.Exists(archive)) File.Delete(archive); throw; }
    }
    public async Task<IReadOnlyList<HistoryEntry>> ListAsync(Guid pairId, CancellationToken token = default)
    {
        var folder = Path.Combine(Root, pairId.ToString("N")); if (!Directory.Exists(folder)) return [];
        var result = new List<HistoryEntry>();
        foreach (var file in Directory.EnumerateFiles(folder, "*.json"))
        {
            token.ThrowIfCancellationRequested();
            await using var stream = File.OpenRead(file);
            var entry = await JsonSerializer.DeserializeAsync<HistoryEntry>(stream, StateStore.Json, token).ConfigureAwait(false);
            if (entry != null && entry.PairId == pairId && PathRules.Contains(folder, entry.ArchivePath)) result.Add(entry);
        }
        return result.OrderByDescending(e => e.CreatedAt).ToArray();
    }
    public async Task RestoreAsync(HistoryEntry entry, string[] allowedRoots, long limitBytes, CancellationToken token = default)
    {
        if (!PathRules.Contains(Root, entry.ArchivePath)) throw new IOException("Версия вне области истории.");
        var root = allowedRoots.FirstOrDefault(r => PathRules.Contains(r, entry.OriginalPath));
        if (root == null) throw new IOException("Исходная папка этой версии больше не входит в текущую пару.");
        PathRules.Root(root); PathRules.RejectLinks(entry.ArchivePath); PathRules.RejectLinks(entry.OriginalPath);
        var parent = Path.GetDirectoryName(entry.OriginalPath)!;
        Directory.CreateDirectory(parent);
        var temporary = Path.Combine(parent, ".sinh-restore-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            var before = File.Exists(entry.OriginalPath) ? await TreeScanner.ReadFileAsync(entry.OriginalPath, entry.Version.RelativePath, token).ConfigureAwait(false) : null;
            await VerifiedCopy.CopyAsync(entry.ArchivePath, temporary, entry.Version, 0, null, "Восстановление", token).ConfigureAwait(false);
            var verified = await TreeScanner.ReadFileAsync(temporary, entry.Version.RelativePath, token).ConfigureAwait(false);
            if (!entry.Version.SameContent(verified)) throw new IOException("История повреждена; восстановление отменено.");
            if (before != null) await ArchiveAsync(entry.PairId, entry.OriginalPath, before, limitBytes, token).ConfigureAwait(false);
            await SyncExecutor.VerifyEntryAsync(entry.OriginalPath, before, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested(); File.Move(temporary, entry.OriginalPath, true);
            File.SetLastWriteTimeUtc(entry.OriginalPath, entry.Version.LastWriteUtc);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
