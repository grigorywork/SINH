using System.Text.Json;

namespace Sinh.Core;

public sealed record HistoryEntry(Guid Id, Guid PairId, string OriginalPath, string ArchivePath, Entry Version, DateTimeOffset CreatedAt,string? RootIdentity=null);

public sealed class HistoryStore(string dataDirectory)
{
    public string Root { get; } = Path.Combine(Path.GetFullPath(dataDirectory), "history");
    private readonly List<string> warnings = [];
    private long? usedBytes;
    public IReadOnlyList<string> Warnings => warnings.AsReadOnly();
    private void Validate(HistoryEntry entry)
    {
        var expected = Path.Combine(Root, entry.PairId.ToString("N"), entry.Id.ToString("N") + ".data");
        if (entry.Id == Guid.Empty || entry.PairId == Guid.Empty || entry.Version == null || entry.Version.Directory || !string.Equals(expected, Path.GetFullPath(entry.ArchivePath), StringComparison.Ordinal)) throw new IOException("Недопустимая запись истории.");
        PathRules.Under(Root, entry.Version.RelativePath);
        PathRules.RejectLinks(entry.ArchivePath);
    }
    public async Task<HistoryEntry> ArchiveAsync(Guid pairId, string originalPath, Entry version, long limitBytes, CancellationToken token,string? rootIdentity=null)
    {
        Directory.CreateDirectory(Root);
        // A store is scoped to one serialized application run. Measure once, account for each committed archive.
        long used = usedBytes ??= Directory.EnumerateFiles(Root, "*.data", SearchOption.AllDirectories).Sum(p => new FileInfo(p).Length);
        if (used + version.Length > limitBytes) throw new IOException("История заполнена. Освободите место в разделе восстановления; текущий файл не заменён.");
        var id = Guid.NewGuid(); var folder = Path.Combine(Root, pairId.ToString("N")); Directory.CreateDirectory(folder);
        var archive = Path.Combine(folder, id.ToString("N") + ".data");
        var owned=false;
        try
        {
            await VerifiedCopy.CopyAsync(originalPath, archive, version, 0, null, "История", token,()=>{owned=true;return Task.CompletedTask;}).ConfigureAwait(false);
            var entry = new HistoryEntry(id, pairId, originalPath, archive, version, DateTimeOffset.UtcNow,rootIdentity);
            await StateStore.AtomicJsonAsync(Path.ChangeExtension(archive, ".json"), entry, token).ConfigureAwait(false);
            usedBytes = used + version.Length;
            return entry;
        }
        catch { usedBytes = null; if (owned&&File.Exists(archive)) File.Delete(archive); throw; }
    }
    public async Task<IReadOnlyList<HistoryEntry>> ListAsync(Guid pairId, CancellationToken token = default)
    {
        warnings.Clear();
        var folder = Path.Combine(Root, pairId.ToString("N")); if (!Directory.Exists(folder)) return [];
        var result = new List<HistoryEntry>();
        foreach (var file in Directory.EnumerateFiles(folder, "*.json"))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                await using var stream = File.OpenRead(file);
                var entry = await JsonSerializer.DeserializeAsync<HistoryEntry>(stream, StateStore.Json, token).ConfigureAwait(false);
                if (entry == null || entry.PairId != pairId || Path.ChangeExtension(entry.ArchivePath, ".json") != file) throw new IOException("Недопустимая запись истории.");
                Validate(entry); result.Add(entry);
            }
            catch (Exception ex) when (ex is JsonException or IOException or ArgumentException or UnauthorizedAccessException)
            { warnings.Add(Path.GetFileName(file) + ": " + ex.Message); }
        }
        return result.OrderByDescending(e => e.CreatedAt).ToArray();
    }
    public async Task RestoreAsync(HistoryEntry entry, string[] allowedRoots, long limitBytes, CancellationToken token = default,Func<bool>? contextAvailable=null)
    {
        Validate(entry);
        if((entry.RootIdentity!=null&&contextAvailable==null)||contextAvailable?.Invoke()==false)throw new IOException("Носитель для восстановления недоступен или изменился.");
        if (!PathRules.Contains(Root, entry.ArchivePath)) throw new IOException("Версия вне области истории.");
        var root = allowedRoots.FirstOrDefault(r => PathRules.Contains(r, entry.OriginalPath));
        if (root == null) throw new IOException("Исходная папка этой версии больше не входит в текущую пару.");
        PathRules.Root(root); PathRules.RejectLinks(entry.ArchivePath); PathRules.RejectLinks(entry.OriginalPath);
        var parent = Path.GetDirectoryName(entry.OriginalPath)!;
        Directory.CreateDirectory(parent);
        var temporary = Path.Combine(parent, ".sinh-restore-" + Guid.NewGuid().ToString("N") + ".tmp");
        var journalPath = Path.Combine(Path.GetFullPath(dataDirectory), "journal", "active", "restore-" + Guid.NewGuid().ToString("N") + ".json");
        var journal = new JournalState(entry.PairId, root, root, "Восстановление файла", 0, 1, temporary, SourceIdentity:entry.RootIdentity, DestinationIdentity:entry.RootIdentity, TemporaryOwned:false, CurrentPath:entry.OriginalPath);
        var owned=false;
        try
        {
            await StateStore.AtomicJsonAsync(journalPath, journal, token).ConfigureAwait(false);
            var before = File.Exists(entry.OriginalPath) ? await TreeScanner.ReadFileAsync(entry.OriginalPath, entry.Version.RelativePath, token).ConfigureAwait(false) : null;
            await VerifiedCopy.CopyAsync(entry.ArchivePath, temporary, entry.Version, 0, null, "Восстановление", token,async()=>
            {
                owned=true; journal=journal with{TemporaryOwned=true};
                await StateStore.AtomicJsonAsync(journalPath,journal,token).ConfigureAwait(false);
            }).ConfigureAwait(false);
            var verified = await TreeScanner.ReadFileAsync(temporary, entry.Version.RelativePath, token).ConfigureAwait(false);
            if (!entry.Version.SameContent(verified)) throw new IOException("История повреждена; восстановление отменено.");
            if (before != null) await ArchiveAsync(entry.PairId, entry.OriginalPath, before, limitBytes, token,entry.RootIdentity).ConfigureAwait(false);
            await SyncExecutor.VerifyEntryAsync(entry.OriginalPath, before, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();if(contextAvailable?.Invoke()==false)throw new IOException("Носитель для восстановления изменился.");File.Move(temporary, entry.OriginalPath, true);owned=false;
            File.SetLastWriteTimeUtc(entry.OriginalPath, entry.Version.LastWriteUtc);
            await StateStore.AtomicJsonAsync(journalPath,journal with{Stage="Завершено",Completed=1,TemporaryPath=null,TemporaryOwned=false},CancellationToken.None).ConfigureAwait(false);
        }
        finally { if (owned&&contextAvailable?.Invoke()!=false&&File.Exists(temporary)) File.Delete(temporary); }
    }
    public Task DeleteAsync(HistoryEntry entry, CancellationToken token = default) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested(); Validate(entry);
        if (File.Exists(entry.ArchivePath)) File.Delete(entry.ArchivePath);
        File.Delete(Path.ChangeExtension(entry.ArchivePath, ".json"));
        usedBytes = null;
    }, token);
}
