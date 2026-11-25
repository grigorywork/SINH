using System.Diagnostics;
using System.Text.Json;
using Sinh.Core;

internal static class BenchmarkChecks
{
    internal static async Task<int> RunAsync(string reportPath, bool plannerOnly = false)
    {
        var root = Path.Combine(Path.GetTempPath(), "sinh-benchmark-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var measurements = new List<object>(); var started = DateTimeOffset.UtcNow;
        try
        {
            foreach (var count in new[] { 1000, 5000, 10000 })
            {
                var a = Path.Combine(root, "plan-a"); var b = Path.Combine(root, "plan-b"); Directory.CreateDirectory(a); Directory.CreateDirectory(b);
                var entries = new List<Entry>(); var stamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                for (var group = 0; group < (count + 99) / 100; group++) entries.Add(new("Folder-" + group, true, 0, "", stamp));
                for (var i = 0; i < count; i++) entries.Add(new(Path.Combine("Folder-" + (i / 100), "File-Документ-" + i.ToString("D6") + ".dat"), false, 4096, new string('A', 64), stamp));
                var source = TreeSnapshot.Create(a, entries); var destination = TreeSnapshot.Create(b, entries); var request = new SyncRequest(Guid.NewGuid(), a, b);
                GC.Collect(); GC.WaitForPendingFinalizers(); var before = GC.GetTotalAllocatedBytes(true); var watch = Stopwatch.StartNew();
                var plan = new SyncPlanner().Build(request, source, destination); watch.Stop(); if (plan.Operations.Count != 0) throw new Exception("Planning benchmark modified identical trees");
                measurements.Add(new { Scenario = "planner-identical", FileCount = count, DirectoryCount = (count + 99) / 100, Milliseconds = watch.Elapsed.TotalMilliseconds, AllocatedBytes = GC.GetTotalAllocatedBytes(true) - before, WorkingSetBytes = Process.GetCurrentProcess().WorkingSet64 });
                Console.WriteLine($"BENCH planner files={count} {watch.Elapsed.TotalMilliseconds:F1}ms");
            }
            if (!plannerOnly)
            {
                foreach (var (name, count, size) in new[] { ("small-files", 1000, 4096), ("large-streams", 2, 64 * 1024 * 1024) })
                {
                    var a = Path.Combine(root, name, "A"); var b = Path.Combine(root, name, "B"); Directory.CreateDirectory(a); Directory.CreateDirectory(b);
                    var block = new byte[Math.Min(size, 1024 * 1024)]; new Random(91827).NextBytes(block);
                    for (var i = 0; i < count; i++)
                    {
                        var folder = Path.Combine(a, "Папка Folder " + (i / 100)); Directory.CreateDirectory(folder);
                        await using var file = new FileStream(Path.Combine(folder, "Документ File " + i + ".bin"), FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true);
                        BitConverter.TryWriteBytes(block.AsSpan(0, 4), i);
                        for (var remaining = size; remaining > 0; remaining -= Math.Min(remaining, block.Length)) await file.WriteAsync(block.AsMemory(0, Math.Min(remaining, block.Length)));
                    }
                    var request = new SyncRequest(Guid.NewGuid(), a, b); var state = new StateStore(Path.Combine(root, name, "data"));
                    var watch = Stopwatch.StartNew(); var plan = await new ComparisonService().CompareAsync(request); var compareMs = watch.Elapsed.TotalMilliseconds;
                    watch.Restart(); var confirmed = await new SyncExecutor(state).ApplyAsync(plan); var applyMs = watch.Elapsed.TotalMilliseconds;
                    watch.Restart(); var repeated = await new ComparisonService().CompareAsync(request, confirmed); var repeatMs = watch.Elapsed.TotalMilliseconds;
                    if (repeated.Operations.Count != 0 || !confirmed.Source.SameContent(confirmed.Destination)) throw new Exception("Benchmark integrity check failed");
                    measurements.Add(new { Scenario = name, FileCount = count, TotalBytes = (long)count * size, CompareMilliseconds = compareMs, ApplyMilliseconds = applyMs, RepeatCompareMilliseconds = repeatMs, WorkingSetBytes = Process.GetCurrentProcess().WorkingSet64, ManagedBytes = GC.GetTotalMemory(false) });
                    Console.WriteLine($"BENCH {name} bytes={(long)count * size} compare={compareMs:F1}ms apply={applyMs:F1}ms repeat={repeatMs:F1}ms");
                }
            }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
            var report = new { StartedAt = started, FinishedAt = DateTimeOffset.UtcNow, OS = System.Runtime.InteropServices.RuntimeInformation.OSDescription, Architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(), Runtime = Environment.Version.ToString(), Seed = 91827, BufferBytes = 131072, Measurements = measurements };
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        finally { Directory.Delete(root, true); }
    }
}
