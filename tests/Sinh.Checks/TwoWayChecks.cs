using Sinh.Core;

internal static class TwoWayChecks
{
    internal static async Task Run(Func<string, Func<Task>, Task> check, Action<bool, string> require, string temp)
    {
        (string A, string B, StateStore Store, SyncRequest Request) Setup(bool deletes = false)
        {
            var root = Path.Combine(temp, Guid.NewGuid().ToString("N")); var a = Path.Combine(root, "A"); var b = Path.Combine(root, "B"); Directory.CreateDirectory(a); Directory.CreateDirectory(b); return (a, b, new(Path.Combine(root, "data")), new(Guid.NewGuid(), a, b, SyncMode.TwoWay, deletes));
        }
        async Task<ConfirmedState> Apply(SyncRequest r, StateStore store, ConfirmedState? state = null) => await new SyncExecutor(store).ApplyAsync(await new ComparisonService().CompareAsync(r, state));
        await check("two-way first union and repeat", async () =>
        {
            var (a, b, store, r) = Setup(); await File.WriteAllTextAsync(Path.Combine(a, "pc"), "a"); await File.WriteAllTextAsync(Path.Combine(b, "usb"), "b"); var state = await Apply(r, store);
            require(File.Exists(Path.Combine(a, "usb")) && File.Exists(Path.Combine(b, "pc")), "Union failed"); require((await new ComparisonService().CompareAsync(r, state)).Operations.Count == 0, "Two-way repeat not idempotent");
        });
        await check("two-way simultaneous changes preserve both versions", async () =>
        {
            var (a, b, store, r) = Setup(); await File.WriteAllTextAsync(Path.Combine(a, "note.txt"), "initial"); var state = await Apply(r, store); await File.WriteAllTextAsync(Path.Combine(a, "note.txt"), "pc edit"); await File.WriteAllTextAsync(Path.Combine(b, "note.txt"), "usb edit");
            var plan = await new ComparisonService().CompareAsync(r, state); require(plan.Conflicts.Count == 1, "Missing conflict"); var after = await new SyncExecutor(store).ApplyAsync(plan);
            var files = Directory.GetFiles(a); require(files.Length == 2, "Lost conflict version"); require((await Task.WhenAll(files.Select(p => File.ReadAllTextAsync(p)))).ToHashSet().SetEquals(["pc edit", "usb edit"]), "Conflict contents lost"); require(after.Source.SameContent(after.Destination), "Sides diverged");
        });
        await check("two-way deletion policy and deletion vs modification", async () =>
        {
            var (a, b, store, r) = Setup(); await File.WriteAllTextAsync(Path.Combine(a, "f"), "initial"); var state = await Apply(r, store); File.Delete(Path.Combine(a, "f")); await Apply(r, store, state); require(File.Exists(Path.Combine(a, "f")), "Deletion propagated without rule");
            r = r with { AllowDeletes = true }; state = await Apply(r, store); File.Delete(Path.Combine(a, "f")); await File.WriteAllTextAsync(Path.Combine(b, "f"), "edited USB"); var plan = await new ComparisonService().CompareAsync(r, state); require(plan.Conflicts.Count == 1, "Deletion/edit conflict missing"); state = await new SyncExecutor(store).ApplyAsync(plan); require(await File.ReadAllTextAsync(Path.Combine(a, "f")) == "edited USB", "Edited version lost");
            File.Delete(Path.Combine(a, "f")); await Apply(r, store, state); require(!File.Exists(Path.Combine(b, "f")), "Explicit deletion not propagated");
        });
        await check("deleting the last file on either side preserves both roots and permits repeat sync", async () =>
        {
            foreach (var deletes in new[] { false, true }) foreach (var sourceSide in new[] { false, true })
            {
                var (a, b, store, request) = Setup(deletes);
                var ar = Path.Combine(a, "Изображения", "Wallpapers"); var br = Path.Combine(b, "Обои");
                Directory.CreateDirectory(ar); Directory.CreateDirectory(br);
                var r = request with { Source = PathRules.StableRoot(ar), Destination = PathRules.StableRoot(br) };
                await File.WriteAllTextAsync(Path.Combine(ar, "Последний English 🌊.txt"), "one file");
                var state = await Apply(r, store);
                File.Delete(Path.Combine(sourceSide ? ar : br, "Последний English 🌊.txt"));
                state = await Apply(r, store, state);
                require(Directory.Exists(ar) && Directory.Exists(br), "Deleting a file removed a configured root");
                require(Directory.GetFiles(ar).Length == (deletes ? 0 : 1) && Directory.GetFiles(br).Length == (deletes ? 0 : 1), "Deletion policy changed");
                require((await new ComparisonService().CompareAsync(r, state)).Operations.Count == 0, "Repeat after last-file deletion failed");
            }
        });
        await check("two-way file/directory conflict preserves whole subtree", async () =>
        {
            var (a, b, store, r) = Setup(); await File.WriteAllTextAsync(Path.Combine(a, "item"), "pc file"); Directory.CreateDirectory(Path.Combine(b, "item")); await File.WriteAllTextAsync(Path.Combine(b, "item", "child"), "usb child"); var state = await Apply(r, store);
            require(await File.ReadAllTextAsync(Path.Combine(a, "item")) == "pc file", "File lost"); require(Directory.GetFiles(a, "child", SearchOption.AllDirectories).Length == 1, "Subtree lost"); require(state.Source.SameContent(state.Destination), "Mixed-type conflict diverged");
        });
    }
}
