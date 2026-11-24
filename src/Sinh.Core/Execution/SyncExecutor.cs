using System.Text.Json;

namespace Sinh.Core;

public sealed record ExecutionOptions(bool History = true, long HistoryLimitBytes = 4L * 1024 * 1024 * 1024, int SpeedLimitMiB = 0, Func<bool>? ContextAvailable = null);
public sealed record JournalState(Guid PairId, string Source, string Destination, string Stage, int Completed, int Total, string? TemporaryPath, string? Error = null);

public sealed class SyncExecutor(StateStore store)
{
    private readonly TreeScanner scanner = new();
    public async Task<ConfirmedState> ApplyAsync(SyncPlan plan, ExecutionOptions? options = null, IProgress<SyncProgress>? progress = null, CancellationToken token = default)
    {
        options ??= new(); var request = plan.Request;
        void CheckContext() { if(options.ContextAvailable?.Invoke()==false)throw new IOException("Носитель отключён или на его месте появился другой диск. Операция остановлена."); }
        CheckContext();
        PathRules.ValidatePair(request.Source, request.Destination);
        if (PathRules.Contains(request.Source, store.DirectoryPath) || PathRules.Contains(request.Destination, store.DirectoryPath)) throw new IOException("Служебная область SINH находится внутри выбранной пары. Выберите более узкую папку.");
        if (plan.Fingerprint != SyncPlanner.Fingerprint(request)) throw new IOException("План не соответствует настройкам пары.");
        var lockDir = Path.Combine(store.DirectoryPath, "locks"); Directory.CreateDirectory(lockDir);
        await using var lease = new FileStream(Path.Combine(lockDir, request.PairId.ToString("N") + ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var run = Guid.NewGuid().ToString("N"); var work = Path.Combine(store.DirectoryPath, "work", run); Directory.CreateDirectory(work);
        var journalPath = Path.Combine(store.DirectoryPath, "journal", run + ".json");
        var journal = new JournalState(request.PairId, request.Source, request.Destination, "Подготовка", 0, plan.Operations.Count, null);
        string? temporary = null;
        try
        {
            await StateStore.AtomicJsonAsync(journalPath, journal, token).ConfigureAwait(false);
            await VerifyTreesAsync(plan, progress, token).ConfigureAwait(false);
            // Stage all changed payloads before any mutation, including future two-way copies.
            var staged = new Dictionary<string, string>();
            foreach (var operation in plan.Operations.Where(o => o.Kind == OperationKind.CopyFile))
            {
                token.ThrowIfCancellationRequested(); var entry = operation.After!;
                if (staged.ContainsKey(entry.Hash)) continue;
                var source = PathRules.Under(operation.SourceRoot!, operation.SourceRelativePath!);
                var path = Path.Combine(work, entry.Hash + ".data");
                await VerifiedCopy.CopyAsync(source, path, entry, options.SpeedLimitMiB, progress, "Подготовка копии", token).ConfigureAwait(false);
                var verified = await TreeScanner.ReadFileAsync(path, entry.RelativePath, token).ConfigureAwait(false);
                if (!entry.SameContent(verified)) throw new IOException("Не пройдена проверка подготовленной копии.");
                staged.Add(entry.Hash, path);
            }
            await VerifyTreesAsync(plan, progress, token).ConfigureAwait(false);
            var history = new HistoryStore(store.DirectoryPath);
            foreach (var operation in plan.Operations)
            {
                token.ThrowIfCancellationRequested(); PathRules.Root(request.Source); PathRules.Root(request.Destination);
                CheckContext();
                if (operation.Root != plan.Source.Root && operation.Root != plan.Destination.Root) throw new IOException("Операция вне разрешённых корней.");
                var target = PathRules.Under(operation.Root, operation.RelativePath);
                await VerifyEntryAsync(target, operation.Before, token).ConfigureAwait(false);
                journal = journal with { Stage = "Применение", TemporaryPath = null };
                await StateStore.AtomicJsonAsync(journalPath, journal, token).ConfigureAwait(false);
                if (operation.Kind == OperationKind.CopyFile)
                {
                    var entry = operation.After!;
                    temporary = Path.Combine(Path.GetDirectoryName(target)!, ".sinh-" + Guid.NewGuid().ToString("N") + ".tmp");
                    journal = journal with { TemporaryPath = temporary };
                    await StateStore.AtomicJsonAsync(journalPath, journal, token).ConfigureAwait(false);
                    await VerifiedCopy.CopyAsync(staged[entry.Hash], temporary, entry, options.SpeedLimitMiB, progress, "Копирование", token).ConfigureAwait(false);
                    var check = await TreeScanner.ReadFileAsync(temporary, entry.RelativePath, token).ConfigureAwait(false);
                    if (!entry.SameContent(check)) throw new IOException("Целевая копия не прошла проверку SHA256.");
                    if (options.History && operation.Before is { Directory: false } old) await history.ArchiveAsync(request.PairId, target, old, options.HistoryLimitBytes, token).ConfigureAwait(false);
                    await VerifyEntryAsync(target, operation.Before, token).ConfigureAwait(false); PathRules.RejectLinks(target);
                    token.ThrowIfCancellationRequested(); CheckContext(); File.Move(temporary, target, true); temporary = null;
                    File.SetLastWriteTimeUtc(target, entry.LastWriteUtc);
                }
                else if (operation.Kind == OperationKind.DeleteFile)
                {
                    if (options.History) await history.ArchiveAsync(request.PairId, target, operation.Before!, options.HistoryLimitBytes, token).ConfigureAwait(false);
                    await VerifyEntryAsync(target, operation.Before, token).ConfigureAwait(false); token.ThrowIfCancellationRequested(); CheckContext(); File.Delete(target);
                }
                else if (operation.Kind == OperationKind.DeleteDirectory) Directory.Delete(target, false);
                else Directory.CreateDirectory(target);
                journal = journal with { Completed = journal.Completed + 1, TemporaryPath = null };
                await StateStore.AtomicJsonAsync(journalPath, journal, token).ConfigureAwait(false);
                progress?.Report(new("Применение", operation.RelativePath, journal.Completed, journal.Total));
            }
            CheckContext();
            var a = await scanner.ScanAsync(request.Source, request.Exclusions, progress, token).ConfigureAwait(false);
            var b = await scanner.ScanAsync(request.Destination, request.Exclusions, progress, token).ConfigureAwait(false);
            // Replanning must be empty. Otherwise do not advance the confirmed baseline.
            if (new SyncPlanner().Build(request, a, b).Operations.Count != 0) throw new IOException("Папки изменились до завершения проверки. Сверьте их повторно; база не обновлена.");
            var state = new ConfirmedState(plan.Fingerprint, a, b, DateTimeOffset.UtcNow);
            await store.SaveAsync(request.PairId, state, token).ConfigureAwait(false);
            await StateStore.AtomicJsonAsync(journalPath, journal with { Stage = "Завершено", TemporaryPath = null }, CancellationToken.None).ConfigureAwait(false);
            return state;
        }
        catch (Exception ex)
        {
            await StateStore.AtomicJsonAsync(journalPath, journal with { Stage = ex is OperationCanceledException ? "Отменено" : "Не завершено", TemporaryPath = temporary, Error = ex.Message }, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        finally
        {
            if (temporary != null && File.Exists(temporary)) File.Delete(temporary);
            if (Directory.Exists(work)) Directory.Delete(work, true);
        }
    }
    private async Task VerifyTreesAsync(SyncPlan plan, IProgress<SyncProgress>? progress, CancellationToken token)
    {
        var a = await scanner.ScanAsync(plan.Request.Source, plan.Request.Exclusions, progress, token).ConfigureAwait(false);
        var b = await scanner.ScanAsync(plan.Request.Destination, plan.Request.Exclusions, progress, token).ConfigureAwait(false);
        if (!a.SameContent(plan.Source) || !b.SameContent(plan.Destination)) throw new IOException("Папки изменились после сравнения. Постройте новый план; ничего не применено.");
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
    public async Task<int> RecoverAsync(CancellationToken token = default)
    {
        var folder = Path.Combine(store.DirectoryPath, "journal"); if (!Directory.Exists(folder)) return 0; var count = 0;
        foreach (var path in Directory.EnumerateFiles(folder, "*.json"))
        {
            JournalState? journal;
            await using (var stream = File.OpenRead(path)) journal = await JsonSerializer.DeserializeAsync<JournalState>(stream, StateStore.Json, token).ConfigureAwait(false);
            if (journal == null || journal.Stage == "Завершено" || journal.Stage == "Восстановлено") continue;
            if (journal.TemporaryPath is { } temp && (PathRules.Contains(journal.Source, temp) || PathRules.Contains(journal.Destination, temp)) && Path.GetFileName(temp).StartsWith(".sinh-", StringComparison.Ordinal) && temp.EndsWith(".tmp", StringComparison.Ordinal))
            { PathRules.RejectLinks(temp); if (File.Exists(temp)) File.Delete(temp); }
            await StateStore.AtomicJsonAsync(path, journal with { Stage = "Восстановлено", TemporaryPath = null }, token).ConfigureAwait(false); count++;
        }
        return count;
    }
}
