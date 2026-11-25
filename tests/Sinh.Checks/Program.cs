using Sinh.Settings;

if (args.Length > 0 && args[0] == "--benchmark") return await BenchmarkChecks.RunAsync(args.Length > 1 ? args[1] : "artifacts/benchmark.json", args.Contains("--planner-only"));

if (args.Length > 0 && args[0] == "--soak")
{
    var seconds = args.Length > 1 ? int.Parse(args[1]) : 3600;
    var seed = args.Length > 2 ? int.Parse(args[2]) : 900000;
    var report = args.Length > 3 ? args[3] : "artifacts/soak-report.json";
    var minimumCases = args.Length > 4 ? int.Parse(args[4]) : 0;
    return await SoakChecks.RunAsync(seconds, seed, report, minimumCases, args.Contains("--no-history"));
}

var failures = 0;
var count = 0;
async Task Check(string name, Func<Task> run)
{
    count++;
    try { await run(); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + ": " + ex); }
}
void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
var temp = Path.Combine(Path.GetTempPath(), "sinh-check-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
try
{
    await Check("safe defaults", async () =>
    {
        using var store = new SettingsStore(Path.Combine(temp, "defaults.json")); await store.LoadAsync();
        Require(!store.Current.AutoStart && store.Current.Pairs.Count == 0, "Automatic start enabled");
        var p = new FolderPair(); Require(p.Mode == SyncMode.Updates && !p.AllowDeletes && !p.AutoOnConnect && p.IntervalMinutes == 0, "Unsafe pair defaults");
    });
    await Check("atomic settings roundtrip, Unicode, schema migration", async () =>
    {
        var path = Path.Combine(temp, "roundtrip.json");
        using (var store = new SettingsStore(path)) await store.UpdateAsync(s => s with { Pairs = [new() { Name = "Капля 🌊", Source = "/α", Destination = "/б" }], Appearance = s.Appearance with { Scale = 1.15, Theme = Theme.Dark } });
        using var loaded = new SettingsStore(path); await loaded.LoadAsync();
        Require(loaded.Current.Pairs[0].Name == "Капля 🌊" && loaded.Current.Appearance.Scale == 1.15, "Roundtrip mismatch");
        await File.WriteAllTextAsync(path, "{\"SchemaVersion\":0,\"Appearance\":{\"Theme\":\"FutureTheme\",\"Scale\":-4}}");
        await loaded.LoadAsync(); Require(loaded.Current.SchemaVersion == 1 && loaded.Current.Appearance.Theme == Theme.System && loaded.Current.Appearance.Scale == 1, "Migration/unknown enum failed");
    });
    await Check("multiple selected folders retain exact names and reject target collisions", () =>
    {
        var a = Path.Combine(temp, "Выбранная папка Я" + new string('a', 100));
        var b = Path.Combine(temp, "English folder 🌊");
        var target = Path.Combine(temp, "Копии");
        var mapped = FolderSelection.Map([a, b], target, true);
        Require(mapped.Count == 2 && mapped[0].Destination == Path.Combine(target, Path.GetFileName(a)) && mapped[1].Source == b, "Names or roots changed");
        Require(FolderSelection.Map([b], target, true)[0].Destination == Path.Combine(target, Path.GetFileName(b)), "Unchecking a folder changed grouping");
        foreach (var sources in new[] { Array.Empty<string>(), new[] { a, Path.Combine(temp, "other", Path.GetFileName(a)) } })
        {
            try { FolderSelection.Map(sources, target, true); throw new Exception("Invalid selection accepted"); }
            catch (ArgumentException) { }
        }
        Require(FolderSelection.Map([a], target, false)[0].Destination == target, "Single-folder target changed");
        return Task.CompletedTask;
    });
    await Check("legacy icons ignored and per-pair USB direction persists independently", async () =>
    {
        var path = Path.Combine(temp, "usb-side.json");
        await File.WriteAllTextAsync(path, "{\"Appearance\":{\"Icon\":\"Glass\"}}");
        using (var store = new SettingsStore(path))
        {
            await store.LoadAsync(); Require(store.RecoveryMessage == null, "Legacy icon broke settings");
            await store.UpdateAsync(s => s with { Pairs = [new() { Source = "A", Destination = "B", UsbSide = UsbSide.Source }, new() { Source = "C", Destination = "D" }] });
        }
        using var reloaded = new SettingsStore(path); await reloaded.LoadAsync();
        Require(reloaded.Current.Pairs[0].UsbSide == UsbSide.Source && reloaded.Current.Pairs[1].UsbSide == UsbSide.Destination, "Pair direction leaked");
        Require(!(await File.ReadAllTextAsync(path)).Contains("\"Icon\""), "Removed icon still exported");
    });
    await Check("corrupt settings preserved", async () =>
    {
        var path = Path.Combine(temp, "broken.json"); await File.WriteAllTextAsync(path, "{broken");
        using var store = new SettingsStore(path); await store.LoadAsync();
        Require(store.RecoveryMessage != null && Directory.GetFiles(temp, "broken.json.recovery-*").Length == 1, "Missing recovery copy");
        Require(await File.ReadAllTextAsync(path) == "{broken", "Original corruption destroyed");
    });
    await Check("rapid updates serialize; subscribers survive GC and unsubscribe", async () =>
    {
        using var store = new SettingsStore(Path.Combine(temp, "rapid.json")); var events = 0;
        EventHandler<AppSettings> handler = (_, _) => Interlocked.Increment(ref events); store.Changed += handler;
        GC.Collect(); GC.WaitForPendingFinalizers();
        await Task.WhenAll(Enumerable.Range(0, 25).Select(i => store.UpdateAsync(s => s with { Pairs = [.. s.Pairs, new() { Name = i.ToString() }] })));
        Require(store.Current.Pairs.Count == 25 && events == 25, "Lost update or observer");
        store.Changed -= handler; await store.UpdateAsync(s => s with { NotifySuccess = false }); Require(events == 25, "Observer not removed");
        Require(!Directory.GetFiles(temp, "*.tmp").Any(), "Staging settings leaked");
    });
    await Check("failed save does not publish new state", async () =>
    {
        var blocked = Path.Combine(temp, "not-a-directory"); await File.WriteAllTextAsync(blocked, "x");
        using var store = new SettingsStore(Path.Combine(blocked, "settings.json"));
        try { await store.UpdateAsync(s => s with { NotifySuccess = false }); throw new Exception("Expected write failure"); }
        catch (IOException) { }
        Require(store.Current.NotifySuccess, "Unpersisted state published");
    });
    await ComparisonChecks.Run(Check, Require, temp);
    await ExecutionChecks.Run(Check, Require, temp);
    await TwoWayChecks.Run(Check, Require, temp);
    await BoundaryChecks.Run(Check, Require, temp);
    await PolicyAndFaultChecks.Run(Check, Require, temp);
    await ExclusionSafetyChecks.Run(Check, Require, temp);
    await HardeningChecks.Run(Check, Require, temp);
}
finally { Directory.Delete(temp, true); }
Console.WriteLine($"{count - failures}/{count} passed");
return failures == 0 ? 0 : 1;
