using Sinh.Core;

internal static class ExclusionSafetyChecks
{
    internal static async Task Run(Func<string, Func<Task>, Task> check, Action<bool, string> require, string temp)
    {
        (string A, string B, string Data) Setup()
        {
            var root = Path.Combine(temp, Guid.NewGuid().ToString("N")); var a = Path.Combine(root, "A"); var b = Path.Combine(root, "B"); Directory.CreateDirectory(a); Directory.CreateDirectory(b); return (a, b, Path.Combine(root, "data"));
        }
        await check("internal temporary namespace is excluded without hiding ordinary user tmp files", async () =>
        {
            var (a, b, data) = Setup(); var internalName = ".sinh-" + Guid.NewGuid().ToString("N") + ".tmp"; var restoreName = ".sinh-restore-" + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllTextAsync(Path.Combine(a, internalName), "unfinished"); await File.WriteAllTextAsync(Path.Combine(a, restoreName), "unfinished restore"); await File.WriteAllTextAsync(Path.Combine(a, ".sinh-user.tmp"), "ordinary user file");
            Directory.CreateDirectory(Path.Combine(b, internalName)); await File.WriteAllTextAsync(Path.Combine(b, internalName, "keep"), "protected counterpart");
            var request = new SyncRequest(Guid.NewGuid(), a, b, SyncMode.Mirror, true); var plan = await new ComparisonService().CompareAsync(request);
            require(plan.SkippedPaths.Contains(internalName) && plan.SkippedPaths.Contains(restoreName), "Internal temporary paths not reported"); await new SyncExecutor(new(data)).ApplyAsync(plan);
            require(await File.ReadAllTextAsync(Path.Combine(b, ".sinh-user.tmp")) == "ordinary user file" && !File.Exists(Path.Combine(b, restoreName)) && await File.ReadAllTextAsync(Path.Combine(b, internalName, "keep")) == "protected counterpart", "Temporary exclusion copied internal data or lost user data");
        });
        await check("mirror preserves excluded file and its necessary container", async () =>
        {
            var (a, b, data) = Setup(); Directory.CreateDirectory(Path.Combine(b, "parent")); await File.WriteAllTextAsync(Path.Combine(b, "parent", "keep.tmp"), "excluded"); await File.WriteAllTextAsync(Path.Combine(b, "parent", "obsolete.txt"), "remove"); var r = new SyncRequest(Guid.NewGuid(), a, b, SyncMode.Mirror, true, ["*.tmp"]);
            var executor = new SyncExecutor(new(data)); await executor.ApplyAsync(await new ComparisonService().CompareAsync(r)); require(await File.ReadAllTextAsync(Path.Combine(b, "parent", "keep.tmp")) == "excluded", "Excluded data deleted"); require(!File.Exists(Path.Combine(b, "parent", "obsolete.txt")), "Ordinary obsolete file not deleted"); require((await new ComparisonService().CompareAsync(r)).Operations.Count == 0, "Protected container repeatedly planned for deletion");
        });
        await check("inner link protects counterpart data without following outside root", async () =>
        {
            var (a, b, data) = Setup(); var outside = Path.Combine(Path.GetDirectoryName(a)!, "outside"); Directory.CreateDirectory(outside); await File.WriteAllTextAsync(Path.Combine(outside, "secret"), "outside roots"); Directory.CreateSymbolicLink(Path.Combine(a, "link"), outside); Directory.CreateDirectory(Path.Combine(b, "link")); await File.WriteAllTextAsync(Path.Combine(b, "link", "keep"), "counterpart"); await File.WriteAllTextAsync(Path.Combine(a, "ordinary"), "copy"); var r = new SyncRequest(Guid.NewGuid(), a, b, SyncMode.Mirror, true);
            var plan = await new ComparisonService().CompareAsync(r); require(plan.SkippedPaths.Contains("link"), "Skipped link not visible in plan"); await new SyncExecutor(new(data)).ApplyAsync(plan);
            require(await File.ReadAllTextAsync(Path.Combine(b, "link", "keep")) == "counterpart" && !File.Exists(Path.Combine(b, "link", "secret")), "Link traversed or counterpart deleted"); require(File.Exists(Path.Combine(b, "ordinary")), "Ordinary file blocked by skipped link");
        });
        await check("two-way deletion cannot remove a container holding excluded data", async () =>
        {
            var (a, b, data) = Setup(); Directory.CreateDirectory(Path.Combine(a, "parent")); Directory.CreateDirectory(Path.Combine(b, "parent")); await File.WriteAllTextAsync(Path.Combine(a, "parent", "normal"), "initial"); await File.WriteAllTextAsync(Path.Combine(b, "parent", "keep.tmp"), "excluded"); var r = new SyncRequest(Guid.NewGuid(), a, b, SyncMode.TwoWay, true, ["*.tmp"]); var executor = new SyncExecutor(new(data)); var state = await executor.ApplyAsync(await new ComparisonService().CompareAsync(r)); Directory.Delete(Path.Combine(a, "parent"), true);
            state = await executor.ApplyAsync(await new ComparisonService().CompareAsync(r, state)); require(await File.ReadAllTextAsync(Path.Combine(b, "parent", "keep.tmp")) == "excluded", "Two-way removed excluded child"); require(!File.Exists(Path.Combine(b, "parent", "normal")), "Ordinary deletion not applied");
        });
        await check("type replacement affecting excluded children fails before writes", async () =>
        {
            var (a, b, data) = Setup(); await File.WriteAllTextAsync(Path.Combine(a, "parent"), "new file"); Directory.CreateDirectory(Path.Combine(b, "parent")); await File.WriteAllTextAsync(Path.Combine(b, "parent", "keep.tmp"), "excluded"); var failed = false;
            try { await new ComparisonService().CompareAsync(new(Guid.NewGuid(), a, b, SyncMode.Mirror, true, ["*.tmp"])); } catch (IOException) { failed = true; }
            require(failed && await File.ReadAllTextAsync(Path.Combine(b, "parent", "keep.tmp")) == "excluded", "Protected subtree type replacement allowed");
        });
    }
}
