using Sinh.Core;

internal static class ExecutionChecks
{
    internal static async Task Run(Func<string, Func<Task>, Task> check, Action<bool, string> require, string temp)
    {
        (string A, string B, string Data) Roots()
        {
            var root = Path.Combine(temp, Guid.NewGuid().ToString("N")); var a = Path.Combine(root, "A"); var b = Path.Combine(root, "B"); var data = Path.Combine(root, "data"); Directory.CreateDirectory(a); Directory.CreateDirectory(b); return (a, b, data);
        }
        await check("verified copy; empty dirs; history restore; idempotent repeat", async () =>
        {
            var (a, b, data) = Roots(); Directory.CreateDirectory(Path.Combine(a, "empty")); await File.WriteAllTextAsync(Path.Combine(a, "🌊.txt"), "new"); await File.WriteAllTextAsync(Path.Combine(b, "🌊.txt"), "old"); await File.WriteAllTextAsync(Path.Combine(b, "extra.txt"), "keep");
            var request = new SyncRequest(Guid.NewGuid(), a, b); var store = new StateStore(data); var executor = new SyncExecutor(store); var plan = await new ComparisonService().CompareAsync(request);
            await executor.ApplyAsync(plan); require(await File.ReadAllTextAsync(Path.Combine(b, "🌊.txt")) == "new", "Copy mismatch"); require(File.Exists(Path.Combine(b, "extra.txt")) && Directory.Exists(Path.Combine(b, "empty")), "Extra/empty dir lost");
            var baseline = await store.LoadAsync(request.PairId); require(baseline != null, "Missing baseline"); require((await new ComparisonService().CompareAsync(request, baseline)).Operations.Count == 0, "Repeat not idempotent");
            var history = new HistoryStore(data); var versions = await history.ListAsync(request.PairId); require(versions.Count == 1, "Old version not archived"); await history.RestoreAsync(versions[0], [a, b], 1L << 30); require(await File.ReadAllTextAsync(Path.Combine(b, "🌊.txt")) == "old", "Restore mismatch"); require((await history.ListAsync(request.PairId)).Count == 2, "Restore overwrote current version without archive");
        });
        await check("stale plan rejected before mutation", async () =>
        {
            var (a, b, data) = Roots(); await File.WriteAllTextAsync(Path.Combine(a, "f"), "new"); await File.WriteAllTextAsync(Path.Combine(b, "f"), "old"); var request = new SyncRequest(Guid.NewGuid(), a, b); var plan = await new ComparisonService().CompareAsync(request);
            await File.WriteAllTextAsync(Path.Combine(b, "f"), "edited after comparison"); var failed = false; try { await new SyncExecutor(new(data)).ApplyAsync(plan); } catch (IOException) { failed = true; }
            require(failed && await File.ReadAllTextAsync(Path.Combine(b, "f")) == "edited after comparison", "Stale plan applied"); require(await new StateStore(data).LoadAsync(request.PairId) == null, "Failed run advanced baseline");
        });
        await check("cancel during large copy preserves original", async () =>
        {
            var (a, b, data) = Roots(); var ap = Path.Combine(a, "large.bin"); await File.WriteAllBytesAsync(ap, new byte[8 * 1024 * 1024]); await File.WriteAllTextAsync(Path.Combine(b, "large.bin"), "old"); var r = new SyncRequest(Guid.NewGuid(), a, b); var plan = await new ComparisonService().CompareAsync(r);
            using var cts = new CancellationTokenSource(); var progress = new InlineProgress(p => { if (p.Stage == "Копирование" && p.Completed > 0) cts.Cancel(); }); var cancelled = false;
            try { await new SyncExecutor(new(data)).ApplyAsync(plan, new(SpeedLimitMiB: 4), progress, cts.Token); } catch (OperationCanceledException) { cancelled = true; }
            require(cancelled, "Cancellation ignored"); require(await File.ReadAllTextAsync(Path.Combine(b, "large.bin")) == "old", "Cancelled copy damaged original"); require(!Directory.GetFiles(b, "*.tmp").Any(), "Temporary destination left behind");
        });
        await check("history limit stops replacement without loss", async () =>
        {
            var (a, b, data) = Roots(); await File.WriteAllTextAsync(Path.Combine(a, "f"), "new"); await File.WriteAllTextAsync(Path.Combine(b, "f"), "old version"); var plan = await new ComparisonService().CompareAsync(new(Guid.NewGuid(), a, b)); var failed = false;
            try { await new SyncExecutor(new(data)).ApplyAsync(plan, new(HistoryLimitBytes: 1)); } catch (IOException) { failed = true; }
            require(failed && await File.ReadAllTextAsync(Path.Combine(b, "f")) == "old version", "History overflow damaged original");
        });
        await check("mirror removes with recoverable file versions", async () =>
        {
            var (a, b, data) = Roots(); Directory.CreateDirectory(Path.Combine(b, "obsolete")); await File.WriteAllTextAsync(Path.Combine(b, "obsolete", "old"), "recover me"); var r = new SyncRequest(Guid.NewGuid(), a, b, SyncMode.Mirror, true);
            await new SyncExecutor(new(data)).ApplyAsync(await new ComparisonService().CompareAsync(r)); require(!Directory.Exists(Path.Combine(b, "obsolete")), "Mirror did not remove directory"); require((await new HistoryStore(data).ListAsync(r.PairId)).Count == 1, "Deleted file not archived");
        });
        await check("case-only rename preserves exact spelling and content", async () =>
        {
            var (a, b, data) = Roots(); Directory.CreateDirectory(Path.Combine(a, "Folder")); Directory.CreateDirectory(Path.Combine(b, "folder")); await File.WriteAllTextAsync(Path.Combine(a, "Folder", "Note.TXT"), "same"); await File.WriteAllTextAsync(Path.Combine(b, "folder", "note.txt"), "same"); var r = new SyncRequest(Guid.NewGuid(), a, b);
            await new SyncExecutor(new(data)).ApplyAsync(await new ComparisonService().CompareAsync(r)); var actual = await new TreeScanner().ScanAsync(b); require(actual.Entries.ContainsKey(Path.Combine("Folder", "Note.TXT")), "Exact case lost"); require(!actual.Entries.ContainsKey("folder"), "Old casing survived");
        });
        await check("file-directory type replacement is explicit and recoverable", async () =>
        {
            var (a, b, data) = Roots(); Directory.CreateDirectory(Path.Combine(a, "item")); await File.WriteAllTextAsync(Path.Combine(a, "item", "child"), "new"); await File.WriteAllTextAsync(Path.Combine(b, "item"), "old file"); var r = new SyncRequest(Guid.NewGuid(), a, b, SyncMode.Mirror, true);
            await new SyncExecutor(new(data)).ApplyAsync(await new ComparisonService().CompareAsync(r)); require(File.Exists(Path.Combine(b, "item", "child")), "Type replacement failed"); require((await new HistoryStore(data).ListAsync(r.PairId)).Count == 1, "Replaced file lost");
        });
        await check("source content changes after planning abort before writes", async () =>
        {
            var (a, b, data) = Roots(); await File.WriteAllBytesAsync(Path.Combine(a, "f"), new byte[2 * 1024 * 1024]); await File.WriteAllTextAsync(Path.Combine(b, "f"), "old"); var r = new SyncRequest(Guid.NewGuid(), a, b); var plan = await new ComparisonService().CompareAsync(r);
            await File.WriteAllBytesAsync(Path.Combine(a, "f"), new byte[1]); var failed = false; try { await new SyncExecutor(new(data)).ApplyAsync(plan); } catch (IOException) { failed = true; }
            require(failed && await File.ReadAllTextAsync(Path.Combine(b, "f")) == "old", "Changed source overwritten destination");
        });
        await check("crash journal recovery only cleans recorded temporary file", async () =>
        {
            var (a, b, data) = Roots(); var tempFile = Path.Combine(b, ".sinh-test.tmp"); await File.WriteAllTextAsync(tempFile, "partial"); await File.WriteAllTextAsync(Path.Combine(b, "user.tmp"), "keep"); Directory.CreateDirectory(Path.Combine(data, "journal"));
            var state = new JournalState(Guid.NewGuid(), a, b, "Применение", 0, 1, tempFile); await File.WriteAllTextAsync(Path.Combine(data, "journal", "crash.json"), System.Text.Json.JsonSerializer.Serialize(state));
            require(await new SyncExecutor(new(data)).RecoverAsync() == 1, "Unfinished run not detected"); require(!File.Exists(tempFile) && File.Exists(Path.Combine(b, "user.tmp")), "Recovery touched unrelated files");
        });
    }
    private sealed class InlineProgress(Action<SyncProgress> report) : IProgress<SyncProgress> { public void Report(SyncProgress value) => report(value); }
}
