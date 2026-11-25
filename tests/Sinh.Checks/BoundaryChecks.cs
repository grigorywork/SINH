using Sinh.Core;

internal static class BoundaryChecks
{
    internal static async Task Run(Func<string, Func<Task>, Task> check, Action<bool, string> require, string temp)
    {
        (string A, string B, string Data) Setup()
        {
            var root = Path.Combine(temp, Guid.NewGuid().ToString("N")); var a = Path.Combine(root, "PC"); var b = Path.Combine(root, "USB", "SINH copies"); Directory.CreateDirectory(a); Directory.CreateDirectory(b); return (a, b, Path.Combine(root, "data"));
        }
        await check("long Russian/English paths, Unicode, exact spelling and restore", async () =>
        {
            var (a, b, data) = Setup(); var folder = string.Join(Path.DirectorySeparatorChar, Enumerable.Range(0, 12).Select(i => "Длинная папка Folder " + i + " mixed")); var file = new string('a', 210) + " русский.txt"; var relative = Path.Combine(folder, file); Directory.CreateDirectory(Path.Combine(a, folder)); await File.WriteAllTextAsync(Path.Combine(a, relative), "До изменения / before"); var r = new SyncRequest(Guid.NewGuid(), a, b);
            var executor = new SyncExecutor(new(data)); await executor.ApplyAsync(await new ComparisonService().CompareAsync(r)); require(Path.Combine(b, relative).Length > 500, "Fixture path is not long"); require(await File.ReadAllTextAsync(Path.Combine(b, relative)) == "До изменения / before", "Long path contents/spelling changed");
            await File.WriteAllTextAsync(Path.Combine(a, relative), "После изменения / after"); await executor.ApplyAsync(await new ComparisonService().CompareAsync(r)); var history = new HistoryStore(data); await history.RestoreAsync((await history.ListAsync(r.PairId))[0], [a, b], 1L << 30); require(await File.ReadAllTextAsync(Path.Combine(b, relative)) == "До изменения / before", "Long-path restore failed");
        });
        await check("unrelated USB files untouched by mirror", async () =>
        {
            var (a, b, data) = Setup(); var usb = Path.GetDirectoryName(b)!; await File.WriteAllTextAsync(Path.Combine(usb, "Личные файлы long English.txt"), "unrelated"); Directory.CreateDirectory(Path.Combine(usb, "Other backup")); await File.WriteAllTextAsync(Path.Combine(usb, "Other backup", "keep"), "keep"); await File.WriteAllTextAsync(Path.Combine(b, "obsolete"), "delete only inside pair"); await File.WriteAllTextAsync(Path.Combine(a, "new"), "new");
            var r = new SyncRequest(Guid.NewGuid(), a, b, SyncMode.Mirror, true); await new SyncExecutor(new(data)).ApplyAsync(await new ComparisonService().CompareAsync(r)); require(await File.ReadAllTextAsync(Path.Combine(usb, "Личные файлы long English.txt")) == "unrelated" && File.Exists(Path.Combine(usb, "Other backup", "keep")), "Unrelated USB data affected");
        });
        await check("long-name two-way conflicts retain both original names", async () =>
        {
            var (a, b, data) = Setup(); var name = new string('x', 235) + ".txt"; await File.WriteAllTextAsync(Path.Combine(a, name), "PC"); await File.WriteAllTextAsync(Path.Combine(b, name), "USB"); var r = new SyncRequest(Guid.NewGuid(), a, b, SyncMode.TwoWay); var state = await new SyncExecutor(new(data)).ApplyAsync(await new ComparisonService().CompareAsync(r));
            require(state.Source.SameContent(state.Destination), "Long conflict sides differ"); var files = Directory.GetFiles(a, "*", SearchOption.AllDirectories); require(files.Length == 2 && files.All(f => Path.GetFileName(f) == name), "Long conflict truncated original filename");
        });
        await check("two-way nested case-only names remain exact", async () =>
        {
            var (a, b, data) = Setup(); Directory.CreateDirectory(Path.Combine(a, "Folder", "Child")); Directory.CreateDirectory(Path.Combine(b, "folder", "child")); await File.WriteAllTextAsync(Path.Combine(a, "Folder", "Child", "Note.txt"), "same"); await File.WriteAllTextAsync(Path.Combine(b, "folder", "child", "note.txt"), "same"); var r = new SyncRequest(Guid.NewGuid(), a, b, SyncMode.TwoWay); var state = await new SyncExecutor(new(data)).ApplyAsync(await new ComparisonService().CompareAsync(r)); require(state.Source.SameContent(state.Destination) && state.Destination.Entries.ContainsKey(Path.Combine("Folder", "Child", "Note.txt")), "Nested case rename diverged");
        });
        await check("two-way child edit survives parent deletion", async () =>
        {
            var (a, b, data) = Setup(); Directory.CreateDirectory(Path.Combine(a, "parent")); await File.WriteAllTextAsync(Path.Combine(a, "parent", "note"), "initial"); var r = new SyncRequest(Guid.NewGuid(), a, b, SyncMode.TwoWay, true); var executor = new SyncExecutor(new(data)); var state = await executor.ApplyAsync(await new ComparisonService().CompareAsync(r)); Directory.Delete(Path.Combine(a, "parent"), true); await File.WriteAllTextAsync(Path.Combine(b, "parent", "note"), "USB edit"); state = await executor.ApplyAsync(await new ComparisonService().CompareAsync(r, state)); require(await File.ReadAllTextAsync(Path.Combine(a, "parent", "note")) == "USB edit" && state.Source.SameContent(state.Destination), "Edited child lost with parent");
        });
        await check("two-way replacing a directory with a file retains the original whole directory", async () =>
        {
            var (a, b, data) = Setup(); Directory.CreateDirectory(Path.Combine(a, "kind")); await File.WriteAllTextAsync(Path.Combine(a, "kind", "child.txt"), "original folder contents");
            var request = new SyncRequest(Guid.NewGuid(), a, b, SyncMode.TwoWay, true); var executor = new SyncExecutor(new(data));
            var state = await executor.ApplyAsync(await new ComparisonService().CompareAsync(request));
            Directory.Delete(Path.Combine(b, "kind"), true); await File.WriteAllTextAsync(Path.Combine(b, "kind"), "replacement USB file");
            var plan = await new ComparisonService().CompareAsync(request, state); state = await executor.ApplyAsync(plan);
            require(File.Exists(Path.Combine(a, "kind", "child.txt")), "Type conflict removed the original directory contents");
            require(state.Source.SameContent(state.Destination), "Type conflict left unequal sides");
        });
        if (OperatingSystem.IsWindows()) await check("Windows extended-path alias cannot bypass nested-root protection", async () =>
        {
            var (a, b, data) = Setup(); var child = Path.Combine(a, "nested"); Directory.CreateDirectory(child); var failed = false;
            try { await new ComparisonService().CompareAsync(new(Guid.NewGuid(), a, @"\\?\" + child)); } catch (IOException) { failed = true; }
            require(failed, "Extended path bypassed overlap protection");
        });
    }
}
