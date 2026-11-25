using Sinh.Core;

internal static class ComparisonChecks
{
    internal static async Task Run(Func<string, Func<Task>, Task> check, Action<bool, string> require, string temp)
    {
        (string A, string B) Roots()
        {
            var root = Path.Combine(temp, Guid.NewGuid().ToString("N")); var a = Path.Combine(root, "A"); var b = Path.Combine(root, "B"); Directory.CreateDirectory(a); Directory.CreateDirectory(b); return (a, b);
        }
        await check("compare is read-only; updates preserve destination extras", async () =>
        {
            var (a, b) = Roots(); await File.WriteAllTextAsync(Path.Combine(a, "новый 🌊.txt"), "new"); await File.WriteAllTextAsync(Path.Combine(b, "keep.txt"), "keep");
            var before = await new TreeScanner().ScanAsync(b);
            var plan = await new ComparisonService().CompareAsync(new(Guid.NewGuid(), a, b));
            require(plan.Operations.Count == 1 && plan.Operations[0].Kind == OperationKind.CopyFile, "Unexpected update plan");
            require(before.SameContent(await new TreeScanner().ScanAsync(b)), "Compare modified destination");
        });
        await check("same size/time files still compared by content", async () =>
        {
            var (a, b) = Roots(); var ap = Path.Combine(a, "same.txt"); var bp = Path.Combine(b, "same.txt"); await File.WriteAllTextAsync(ap, "aaa"); await File.WriteAllTextAsync(bp, "bbb");
            File.SetLastWriteTimeUtc(bp, File.GetLastWriteTimeUtc(ap));
            var plan = await new ComparisonService().CompareAsync(new(Guid.NewGuid(), a, b)); require(plan.Operations.Count == 1, "Hash comparison missed change");
        });
        await check("mirror deletions require explicit profile rule", async () =>
        {
            var (a, b) = Roots(); await File.WriteAllTextAsync(Path.Combine(b, "extra.txt"), "x");
            var service = new ComparisonService(); var r = new SyncRequest(Guid.NewGuid(), a, b, SyncMode.Mirror);
            require((await service.CompareAsync(r)).Deletes == 0, "Deleted without rule"); require((await service.CompareAsync(r with { AllowDeletes = true })).Deletes == 1, "Missing mirror deletion");
        });
        await check("offline root and nested roots rejected", async () =>
        {
            var (a, b) = Roots(); Directory.Delete(b); var failed = false;
            try { await new ComparisonService().CompareAsync(new(Guid.NewGuid(), a, b, SyncMode.Mirror, true)); } catch (IOException) { failed = true; }
            require(failed, "Offline root accepted as empty");
            var nested = Path.Combine(a, "inside"); Directory.CreateDirectory(nested); failed = false;
            try { await new ComparisonService().CompareAsync(new(Guid.NewGuid(), a, nested)); } catch (IOException) { failed = true; }
            require(failed, "Nested roots accepted");
        });
        await check("links and ancestor links do not escape roots", async () =>
        {
            var (a, b) = Roots(); Directory.CreateSymbolicLink(Path.Combine(a, "link"), b);
            var plan = await new ComparisonService().CompareAsync(new(Guid.NewGuid(), a, b));
            require(plan.Operations.Count == 0 && plan.SkippedPaths.Contains("link"), "Inner link was followed or not reported");
            var failed = false;
            try { await new ComparisonService().CompareAsync(new(Guid.NewGuid(), Path.Combine(a, "link"), a)); } catch (IOException) { failed = true; }
            require(failed, "Configured root link accepted");
            require(!PathRules.Contains(a, Path.Combine(a + "other", "x")), "Sibling prefix treated as nested");
            failed = false; try { PathRules.Under(a, "../escape"); } catch (IOException) { failed = true; }
            require(failed, "Traversal accepted");
        });
        await check("exclusions and empty directories preserved in snapshots", async () =>
        {
            var (a, b) = Roots(); Directory.CreateDirectory(Path.Combine(a, "empty")); await File.WriteAllTextAsync(Path.Combine(a, "ignored.tmp"), "x");
            var plan = await new ComparisonService().CompareAsync(new(Guid.NewGuid(), a, b, Exclusions: ["*.tmp"]));
            require(plan.Operations.Count == 1 && plan.Operations[0].Kind == OperationKind.CreateDirectory, "Excluded file planned / empty dir omitted");
        });
        await check("destination changes detected against confirmed state", async () =>
        {
            var (a, b) = Roots(); var request = new SyncRequest(Guid.NewGuid(), a, b); var service = new ComparisonService(); var initial = await service.CompareAsync(request);
            var state = new ConfirmedState(initial.Fingerprint, initial.Source, initial.Destination, DateTimeOffset.UtcNow);
            await File.WriteAllTextAsync(Path.Combine(b, "usb-edit.txt"), "changed on USB");
            require((await service.CompareAsync(request, state)).DestinationChanged, "USB changes not detected");
        });
        await check("cancellation never returns partial comparison", async () =>
        {
            var (a, b) = Roots(); using var cancellation = new CancellationTokenSource(); cancellation.Cancel(); var cancelled = false;
            try { await new ComparisonService().CompareAsync(new(Guid.NewGuid(), a, b), cancellationToken: cancellation.Token); } catch (OperationCanceledException) { cancelled = true; }
            require(cancelled, "Cancelled scan returned plan");
        });
    }
}
