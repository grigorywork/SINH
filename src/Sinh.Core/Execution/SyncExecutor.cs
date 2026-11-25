using System.Text.Json;

namespace Sinh.Core;

public sealed record ExecutionOptions(bool History = true, long HistoryLimitBytes = 4L * 1024 * 1024 * 1024, int SpeedLimitMiB = 0, Func<bool>? ContextAvailable = null, long MaxFileBytes = long.MaxValue, int MaxComponentLength = int.MaxValue, bool DestinationReadOnly = false);
public sealed record JournalState(Guid PairId, string Source, string Destination, string Stage, int Completed, int Total, string? TemporaryPath, string? Error = null, string? SourceIdentity = null, string? DestinationIdentity = null, bool TemporaryOwned = true, string? CurrentPath = null);

public sealed class SyncExecutor(StateStore store)
{
    private readonly List<string> recoveryWarnings = [];
    public IReadOnlyList<string> RecoveryWarnings => recoveryWarnings.AsReadOnly();
    public async Task<ConfirmedState> ApplyAsync(SyncPlan plan, ExecutionOptions? options = null, IProgress<SyncProgress>? progress = null, CancellationToken token = default)
    {
        options ??= new(); var request = plan.Request;
        void CheckContext() { if (options.ContextAvailable?.Invoke() == false) throw new IOException("Носитель отключён или на его месте появился другой диск. Операция остановлена."); }
        CheckContext();
        PathRules.ValidatePair(request.Source, request.Destination);
        if (options.DestinationReadOnly && plan.Operations.Count > 0) throw new IOException("Носитель назначения доступен только для чтения.");
        foreach (var operation in plan.Operations)
        {
            if (operation.Kind == OperationKind.CopyFile && operation.Bytes > options.MaxFileBytes) throw new IOException("Файл превышает ограничение файловой системы назначения: " + operation.RelativePath);
            if (operation.Kind is OperationKind.CopyFile or OperationKind.CreateDirectory && operation.RelativePath.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]).Any(component => component.Length > options.MaxComponentLength)) throw new IOException("Имя превышает ограничение файловой системы назначения: " + operation.RelativePath);
        }
        SyncScope.ValidateInternalDirectory(request, store.DirectoryPath);
        if (plan.Fingerprint != SyncPlanner.Fingerprint(request)) throw new IOException("План не соответствует настройкам пары.");
        var lockDir = Path.Combine(store.DirectoryPath, "locks"); Directory.CreateDirectory(lockDir);
        await using var lease = new FileStream(Path.Combine(lockDir, request.PairId.ToString("N") + ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var run = Guid.NewGuid().ToString("N"); var work = Path.Combine(store.DirectoryPath, "work", run); Directory.CreateDirectory(work);
        var journalPath = Path.Combine(store.DirectoryPath, "journal", "active", run + ".json");
        var journal = new JournalState(request.PairId, request.Source, request.Destination, "Подготовка", 0, plan.Operations.Count, null, SourceIdentity: request.SourceIdentity, DestinationIdentity: request.DestinationIdentity, TemporaryOwned: false);
        string? temporary = null;
        var temporaryOwned = false;
        try
        {
            await StateStore.AtomicJsonAsync(journalPath, journal, token).ConfigureAwait(false);
            await VerifyTreesAsync(plan, progress, token).ConfigureAwait(false);
            var payloads = await PayloadSources.PrepareAsync(plan, work, options, progress, token).ConfigureAwait(false);
            if (payloads.Staged) await VerifyTreesAsync(plan, progress, token).ConfigureAwait(false);
            var history = new HistoryStore(store.DirectoryPath);
            var lastProgress = Environment.TickCount64;
            foreach (var operation in plan.Operations)
            {
                token.ThrowIfCancellationRequested(); PathRules.Root(request.Source); PathRules.Root(request.Destination);
                CheckContext();
                if (operation.Root != plan.Source.Root && operation.Root != plan.Destination.Root) throw new IOException("Операция вне разрешённых корней.");
                var target = PathRules.Under(operation.Root, operation.RelativePath);
                await VerifyEntryAsync(target, operation.Before, token).ConfigureAwait(false);
                journal = journal with { Stage = "Применение", TemporaryPath = null, TemporaryOwned = false, CurrentPath = target };
                await StateStore.AtomicJsonAsync(journalPath, journal, token).ConfigureAwait(false);
                if (operation.Kind == OperationKind.CopyFile)
                {
                    var entry = operation.After!;
                    temporary = Path.Combine(Path.GetDirectoryName(target)!, ".sinh-" + Guid.NewGuid().ToString("N") + ".tmp");
                    temporaryOwned = false;
                    journal = journal with { TemporaryPath = temporary, TemporaryOwned = false };
                    await StateStore.AtomicJsonAsync(journalPath, journal, token).ConfigureAwait(false);
                    await VerifiedCopy.CopyAsync(payloads.Files[entry.Hash], temporary, entry, options.SpeedLimitMiB, progress, "Копирование", token, async () =>
                    {
                        temporaryOwned = true; journal = journal with { TemporaryOwned = true };
                        await StateStore.AtomicJsonAsync(journalPath, journal, token).ConfigureAwait(false);
                    }).ConfigureAwait(false);
                    var check = await TreeScanner.ReadFileAsync(temporary, entry.RelativePath, token).ConfigureAwait(false);
                    if (!entry.SameContent(check)) throw new IOException("Целевая копия не прошла проверку SHA256.");
                    if (options.History && operation.Before is { Directory: false } old) await history.ArchiveAsync(request.PairId, target, old, options.HistoryLimitBytes, token, operation.Root == plan.Source.Root ? request.SourceIdentity : request.DestinationIdentity).ConfigureAwait(false);
                    await VerifyEntryAsync(target, operation.Before, token).ConfigureAwait(false); PathRules.RejectLinks(target);
                    token.ThrowIfCancellationRequested(); CheckContext(); File.Move(temporary, target, true); temporary = null; temporaryOwned = false;
                    File.SetLastWriteTimeUtc(target, entry.LastWriteUtc);
                }
                else if (operation.Kind == OperationKind.DeleteFile)
                {
                    if (options.History) await history.ArchiveAsync(request.PairId, target, operation.Before!, options.HistoryLimitBytes, token, operation.Root == plan.Source.Root ? request.SourceIdentity : request.DestinationIdentity).ConfigureAwait(false);
                    await VerifyEntryAsync(target, operation.Before, token).ConfigureAwait(false); token.ThrowIfCancellationRequested(); CheckContext(); File.Delete(target);
                }
                else if (operation.Kind == OperationKind.DeleteDirectory) Directory.Delete(target, false);
                else Directory.CreateDirectory(target);
                journal = journal with { Completed = journal.Completed + 1, TemporaryPath = null, TemporaryOwned = false };
                await StateStore.AtomicJsonAsync(journalPath, journal, token).ConfigureAwait(false);
                if (journal.Completed == 1 || journal.Completed == journal.Total || Environment.TickCount64 - lastProgress >= 100)
                {
                    progress?.Report(new("Применение", operation.RelativePath, journal.Completed, journal.Total));
                    lastProgress = Environment.TickCount64;
                }
            }
            CheckContext();
            var (a, b) = await new ComparisonService().ScanPairAsync(request, progress, token).ConfigureAwait(false);
            // Replanning must be empty. Otherwise do not advance the confirmed baseline.
            if (new SyncPlanner().Build(request, a, b).Operations.Count != 0) throw new IOException("Папки изменились до завершения проверки. Сверьте их повторно; база не обновлена.");
            var state = new ConfirmedState(plan.Fingerprint, a, b, DateTimeOffset.UtcNow);
            await store.SaveAsync(request.PairId, state, token).ConfigureAwait(false);
            await StateStore.AtomicJsonAsync(journalPath, journal with { Stage = "Завершено", TemporaryPath = null }, CancellationToken.None).ConfigureAwait(false);
            ArchiveJournal(journalPath);
            return state;
        }
        catch (Exception ex)
        {
            await StateStore.AtomicJsonAsync(journalPath, journal with { Stage = ex is OperationCanceledException ? "Отменено" : "Не завершено", TemporaryPath = temporary, TemporaryOwned = temporaryOwned, Error = ex.Message }, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        finally
        {
            if (temporaryOwned && temporary != null && options.ContextAvailable?.Invoke() != false && File.Exists(temporary)) File.Delete(temporary);
            if (Directory.Exists(work)) Directory.Delete(work, true);
        }
    }
    private async Task VerifyTreesAsync(SyncPlan plan, IProgress<SyncProgress>? progress, CancellationToken token)
    {
        var (a, b) = await new ComparisonService().ScanPairAsync(plan.Request, progress, token).ConfigureAwait(false);
        if (!a.SameScan(plan.Source) || !b.SameScan(plan.Destination)) throw new IOException("Папки изменились после сравнения. Постройте новый план; ничего не применено.");
    }
    internal static async Task VerifyEntryAsync(string path, Entry? expected, CancellationToken token)
    {
        PathRules.RejectLinks(path);
        if (expected == null)
        {
            if (File.Exists(path) || Directory.Exists(path)) throw new IOException("Вместо свободного пути появился файл или папка: " + path);
            return;
        }
        if (expected.Directory) { if (!Directory.Exists(path)) throw new IOException("Папка исчезла: " + path); }
        else
        {
            var current = await TreeScanner.ReadFileAsync(path, expected.RelativePath, token).ConfigureAwait(false);
            if (!expected.SameContent(current)) throw new IOException("Файл изменился после сравнения: " + path);
        }
    }
    private void ArchiveJournal(string path)
    {
        var folder = Path.Combine(store.DirectoryPath, "journal", "completed"); Directory.CreateDirectory(folder); File.Move(path, Path.Combine(folder, Path.GetFileName(path)), true);
    }
    public async Task<int> RecoverAsync(CancellationToken token = default, Func<JournalState, bool>? contextAvailable = null)
    {
        recoveryWarnings.Clear(); var folder = Path.Combine(store.DirectoryPath, "journal"); if (!Directory.Exists(folder)) return 0; var count = 0;
        var files = Directory.EnumerateFiles(folder, "*.json").Concat(Directory.Exists(Path.Combine(folder, "active")) ? Directory.EnumerateFiles(Path.Combine(folder, "active"), "*.json") : []).ToArray();
        foreach (var path in files)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                JournalState? journal;
                await using (var stream = File.OpenRead(path)) journal = await JsonSerializer.DeserializeAsync<JournalState>(stream, StateStore.Json, token).ConfigureAwait(false);
                if (journal == null || journal.PairId == Guid.Empty || string.IsNullOrEmpty(journal.Source) || string.IsNullOrEmpty(journal.Destination)) throw new JsonException("Invalid journal");
                if (journal.Stage is "Завершено" or "Восстановлено") { ArchiveJournal(path); continue; }
                if (journal.TemporaryPath is { } temp && journal.TemporaryOwned)
                {
                    var root = PathRules.Contains(journal.Destination, temp) ? journal.Destination : PathRules.Contains(journal.Source, temp) ? journal.Source : null;
                    if (root == null || !Path.GetFileName(temp).StartsWith(".sinh-", StringComparison.Ordinal) || !temp.EndsWith(".tmp", StringComparison.Ordinal)) throw new JsonException("Temporary path outside journal roots");
                    if (!Directory.Exists(root)) continue;
                    if (contextAvailable != null && !contextAvailable(journal)) continue;
                    if (contextAvailable == null && (journal.SourceIdentity != null || journal.DestinationIdentity != null)) continue;
                    PathRules.RejectLinks(temp); if (File.Exists(temp)) File.Delete(temp);
                }
                await StateStore.AtomicJsonAsync(path, journal with { Stage = "Восстановлено", TemporaryPath = null, TemporaryOwned = false }, token).ConfigureAwait(false); ArchiveJournal(path); count++;
            }
            catch (JsonException ex)
            {
                recoveryWarnings.Add(Path.GetFileName(path) + ": " + ex.Message);
                try { File.Move(path, path + ".corrupt-" + Guid.NewGuid().ToString("N")); } catch (Exception moveError) when (moveError is IOException or UnauthorizedAccessException) { recoveryWarnings.Add(moveError.Message); }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { recoveryWarnings.Add(Path.GetFileName(path) + ": " + ex.Message); }
        }
        return count;
    }
    public Task PruneCompletedLogsAsync(int days, CancellationToken token = default) => Task.Run(() =>
    {
        var folder = Path.Combine(store.DirectoryPath, "journal", "completed"); if (!Directory.Exists(folder)) return;
        var threshold = DateTime.UtcNow.AddDays(-Math.Clamp(days, 1, 3650));
        foreach (var file in Directory.EnumerateFiles(folder, "*.json")) { token.ThrowIfCancellationRequested(); if (File.GetLastWriteTimeUtc(file) < threshold) File.Delete(file); }
    }, token);
}
