using System.Diagnostics;
using System.Text.Json;
using Sinh.Core;

internal static class SoakChecks
{
    internal static async Task<int> RunAsync(int seconds, int seed, string reportPath)
    {
        var started = DateTimeOffset.UtcNow;
        var clock = Stopwatch.StartNew();
        var iterations = 0; var cancellations = 0; var guarded = 0; var failures = new List<object>();
        var failureCount = 0;
        var assemblyHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(typeof(SyncExecutor).Assembly.Location)));
        var samples = new List<object>(); var lastReport = TimeSpan.Zero;
        var root = Path.Combine(Path.GetTempPath(), "sinh-soak-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        async Task SaveReport(bool completed)
        {
            var report = new { StartedAt = started, UpdatedAt = DateTimeOffset.UtcNow, Completed = completed, DurationSeconds = clock.Elapsed.TotalSeconds, RequestedSeconds = seconds, Seed = seed, Iterations = iterations, Cancellations = cancellations, Guarded = guarded, FailureCount = failureCount, Failures = failures, Samples = samples, OS = System.Runtime.InteropServices.RuntimeInformation.OSDescription, Architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(), CoreVersion = typeof(SyncExecutor).Assembly.GetName().Version?.ToString(), CoreSHA256 = assemblyHash, FixtureRoot = root };
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
            await File.WriteAllTextAsync(reportPath + ".tmp", JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(reportPath + ".tmp", reportPath, true);
        }
        Console.WriteLine($"SOAK START {started:O} duration={seconds}s seed={seed} root={root}");
        while (clock.Elapsed < TimeSpan.FromSeconds(seconds))
        {
            var caseSeed = seed + iterations;
            var folder = Path.Combine(root, iterations.ToString("D8"));
            try
            {
                var result = await RunCase(folder, caseSeed);
                if (result == "cancelled") cancellations++;
                if (result == "guarded") guarded++;
                Directory.Delete(folder, true);
            }
            catch (Exception ex)
            {
                failureCount++;
                if (failures.Count < 10)
                {
                    failures.Add(new { Seed = caseSeed, Fixture = folder, Error = ex.ToString() });
                    Console.WriteLine($"SOAK FAIL seed={caseSeed} fixture={folder} {ex}");
                }
                else if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
            iterations++;
            if (clock.Elapsed - lastReport >= TimeSpan.FromSeconds(30))
            {
                var memory = Process.GetCurrentProcess().WorkingSet64;
                samples.Add(new { ElapsedSeconds = clock.Elapsed.TotalSeconds, Iterations = iterations, Cancellations = cancellations, Guarded = guarded, Failures = failureCount, WorkingSetBytes = memory });
                Console.WriteLine($"SOAK {clock.Elapsed.TotalSeconds:F0}s cases={iterations} cancellations={cancellations} guarded={guarded} failures={failureCount} workingSetMiB={memory / 1048576d:F1}");
                lastReport = clock.Elapsed;
                await SaveReport(false);
            }
        }
        await SaveReport(true);
        Console.WriteLine($"SOAK END cases={iterations} failures={failureCount} report={Path.GetFullPath(reportPath)}");
        if (failureCount == 0) Directory.Delete(root, true);
        return failureCount == 0 ? 0 : 1;
    }

    private static async Task<string> RunCase(string folder, int seed)
    {
        var random = new Random(seed);
        var a = Path.Combine(folder, "A"); var b = Path.Combine(folder, "USB", "Copies");
        Directory.CreateDirectory(a); Directory.CreateDirectory(b);
        var outside = Path.Combine(folder, "USB", "посторонний файл.txt"); await File.WriteAllTextAsync(outside, "unrelated USB payload");
        string[] paths = ["note.txt", "Документы/Общий файл.txt", "English/report.md", "folder/item.bin", "tree/leaf/data.txt", "Смешанный Mixed 🌊.txt", "kind"];
        string Native(string path) => path.Replace('/', Path.DirectorySeparatorChar);
        void Mutate(string root)
        {
            var relative = Native(paths[random.Next(paths.Length)]); var path = Path.Combine(root, relative);
            var action = random.Next(6);
            if (action == 0) { if (File.Exists(path)) File.Delete(path); else if (Directory.Exists(path)) Directory.Delete(path, true); return; }
            var parent = Path.GetDirectoryName(path)!;
            var missing = new Stack<string>(); var walk = parent;
            while (!Directory.Exists(walk)) { missing.Push(walk); walk = Path.GetDirectoryName(walk)!; }
            while (missing.TryPop(out var directory)) { if (File.Exists(directory)) File.Delete(directory); Directory.CreateDirectory(directory); }
            if (action == 1)
            {
                if (File.Exists(path)) File.Delete(path); Directory.CreateDirectory(path);
                File.WriteAllText(Path.Combine(path, "child.txt"), "child " + seed + " / " + random.Next()); return;
            }
            if (Directory.Exists(path)) Directory.Delete(path, true);
            File.WriteAllText(path, "seed=" + seed + " version=" + random.Next() + " Unicode Привет Hello 🌊");
        }
        for (var i = 0; i < 10; i++) Mutate(random.Next(2) == 0 ? a : b);
        var mode = (SyncMode)random.Next(3); var allowDeletes = random.Next(2) == 0;
        var request = new SyncRequest(Guid.NewGuid(), a, b, mode, allowDeletes);
        var store = new StateStore(Path.Combine(folder, "app-data")); var executor = new SyncExecutor(store); var compare = new ComparisonService(); var scanner = new TreeScanner();
        ConfirmedState state;
        try { state = await executor.ApplyAsync(await compare.CompareAsync(request)); }
        catch (IOException ex) when (ex.Message.Contains("для замены требуется", StringComparison.Ordinal)) { return "guarded"; }
        for (var i = 0; i < 7; i++) Mutate(random.Next(2) == 0 ? a : b);
        var beforeA = await scanner.ScanAsync(a); var beforeB = await scanner.ScanAsync(b);
        var protectedTypeCollisionHashes = new HashSet<string>();
        if (mode == SyncMode.TwoWay)
        {
            foreach (var e in beforeA.Entries.Values)
            {
                var other = beforeB.Find(e.RelativePath);
                if (other == null || other.Directory == e.Directory) continue;
                foreach (var v in beforeA.Entries.Values.Concat(beforeB.Entries.Values).Where(v => !v.Directory && (v.RelativePath == e.RelativePath || v.RelativePath.StartsWith(e.RelativePath + Path.DirectorySeparatorChar, StringComparison.Ordinal)))) protectedTypeCollisionHashes.Add(v.Hash);
            }
        }
        SyncPlan plan;
        try { plan = await compare.CompareAsync(request, state); }
        catch (IOException ex) when (ex.Message.Contains("для замены требуется", StringComparison.Ordinal)) { return "guarded"; }
        if (plan.DestinationChanged == beforeB.SameContent(state.Destination)) throw new Exception("Destination-change signal disagrees with independent snapshot");
        using var cts = new CancellationTokenSource();
        var injectCancellation = random.Next(5) == 0;
        var progress = new ImmediateProgress(p => { if (injectCancellation && p.Stage == "Применение" && p.Completed >= 1) cts.Cancel(); });
        var cancelled = false;
        try { await executor.ApplyAsync(plan, progress: progress, token: cts.Token); }
        catch (OperationCanceledException) { cancelled = true; }
        if (await File.ReadAllTextAsync(outside) != "unrelated USB payload") throw new Exception("Unrelated USB file changed");
        var afterA = await scanner.ScanAsync(a); var afterB = await scanner.ScanAsync(b);
        if (mode != SyncMode.TwoWay && !beforeA.SameContent(afterA)) throw new Exception("One-way sync modified its source");
        var validHashes = beforeA.Entries.Values.Concat(beforeB.Entries.Values).Where(e => !e.Directory).Select(e => e.Hash).ToHashSet();
        if (afterA.Entries.Values.Concat(afterB.Entries.Values).Any(e => !e.Directory && !validHashes.Contains(e.Hash))) throw new Exception("Truncated or unknown file content after application");
        if (cancelled)
        {
            var saved = await store.LoadAsync(request.PairId);
            if (saved?.ConfirmedAt != state.ConfirmedAt) throw new Exception("Cancellation advanced confirmed baseline");
            await executor.RecoverAsync(); return "cancelled";
        }
        if (mode == SyncMode.TwoWay && !protectedTypeCollisionHashes.IsSubsetOf(afterA.Entries.Values.Where(e => !e.Directory).Select(e => e.Hash))) throw new Exception("Two-way type collision lost a file/folder version from the visible trees");
        if (mode == SyncMode.TwoWay || mode == SyncMode.Mirror && allowDeletes)
        { if (!afterA.SameContent(afterB)) throw new Exception("Completed exact sync left different trees"); }
        else
        {
            foreach (var e in afterA.Entries.Values) if (!e.SameContent(afterB.Find(e.RelativePath))) throw new Exception("Source entry not copied with exact spelling: " + e.RelativePath);
        }
        var repeated = await compare.CompareAsync(request, await store.LoadAsync(request.PairId));
        if (repeated.Operations.Count != 0) throw new Exception("Repeated successful operation is not idempotent");
        return "completed";
    }
    private sealed class ImmediateProgress(Action<SyncProgress> action) : IProgress<SyncProgress> { public void Report(SyncProgress value) => action(value); }
}
