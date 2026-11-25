using Sinh.Core;
using Sinh.Settings;
using System.Text.Json;
using CoreMode = Sinh.Core.SyncMode;

internal static class HardeningChecks
{
    internal static async Task Run(Func<string, Func<Task>, Task> check, Action<bool, string> require, string temp)
    {
        (string A, string B, string Data) Setup()
        {
            var root = Path.Combine(temp, Guid.NewGuid().ToString("N")); var a = Path.Combine(root, "A"); var b = Path.Combine(root, "B"); Directory.CreateDirectory(a); Directory.CreateDirectory(b); return (a, b, Path.Combine(root, "data"));
        }
        await check("settings publish immutable collections and ignore no-op saves", async () =>
        {
            var path = Path.Combine(temp, "immutable-" + Guid.NewGuid() + ".json"); using var store = new SettingsStore(path); var exclusions = new[] { "*.tmp" }; var pair = new FolderPair { Source = "/a", Destination = "/b", Exclusions = exclusions }; var pairs = new[] { pair }; await store.UpdateAsync(s => s with { Pairs = pairs }); pairs[0] = pair with { Name = "mutated" }; exclusions[0] = "*";
            require(store.Current.Pairs[0].Name != "mutated" && store.Current.Pairs[0].Exclusions[0] == "*.tmp", "Published settings mutated externally");
            var refused = false; try { ((IList<FolderPair>)store.Current.Pairs).Clear(); } catch (NotSupportedException) { refused = true; }
            require(refused, "Settings collections writable");
            await store.UpdateAsync(s => s); require(!File.Exists(path + ".bak"), "No-op update rewrote settings");
        });
        await check("corrupt settings recovery preserves prior valid backup", async () =>
        {
            var path = Path.Combine(temp, "preserve-" + Guid.NewGuid() + ".json"); using var store = new SettingsStore(path); await store.UpdateAsync(s => s with { Appearance = s.Appearance with { Theme = Theme.Dark } }); await store.UpdateAsync(s => s with { Appearance = s.Appearance with { Theme = Theme.Light } }); var validBackup = await File.ReadAllTextAsync(path + ".bak"); await File.WriteAllTextAsync(path, "{broken"); await store.LoadAsync(); await store.UpdateAsync(s => s with { NotifySuccess = false }); require(await File.ReadAllTextAsync(path + ".bak") == validBackup, "Recovery overwrote valid backup with corrupt file");
        });
        await check("whole profile excludes its own SINH service area", async () =>
        {
            var (a, b, _) = Setup(); var data = Path.Combine(a, "AppData", "Local", "SINH"); Directory.CreateDirectory(data); await File.WriteAllTextAsync(Path.Combine(data, "settings.json"), "own metadata"); await File.WriteAllTextAsync(Path.Combine(a, "document.txt"), "user data"); var r = SyncScope.ExcludeInternalDirectory(new(Guid.NewGuid(), a, b), data); await new SyncExecutor(new(data)).ApplyAsync(await new ComparisonService().CompareAsync(r));
            require(File.Exists(Path.Combine(b, "document.txt")) && !Directory.Exists(Path.Combine(b, "AppData", "Local", "SINH")), "Own metadata entered backup"); require(await File.ReadAllTextAsync(Path.Combine(data, "settings.json")) == "own metadata", "Own config overwritten");
        });
        await check("watch exclusions cover ancestors without matching unrelated siblings", () =>
        {
            var rules = new ExclusionRules(["cache", "*.tmp"]); require(rules.MatchesPathOrParent(Path.Combine("cache", "nested", "file")), "Excluded subtree event accepted"); require(!rules.MatchesPathOrParent(Path.Combine("other-cache", "file")), "Unrelated event suppressed"); return Task.CompletedTask;
        });
        await check("recovery defers cleanup on a changed drive", async () =>
        {
            var (a, b, data) = Setup(); var file = Path.Combine(b, ".sinh-owned.tmp"); await File.WriteAllTextAsync(file, "partial"); var folder = Path.Combine(data, "journal", "active"); Directory.CreateDirectory(folder); var journal = new JournalState(Guid.NewGuid(), a, b, "Применение", 0, 1, file, DestinationIdentity: "volume:" + Guid.NewGuid() + "/copy"); await File.WriteAllTextAsync(Path.Combine(folder, "pending.json"), JsonSerializer.Serialize(journal)); var engine = new SyncExecutor(new(data)); require(await engine.RecoverAsync(contextAvailable: _ => false) == 0 && File.Exists(file), "Wrong drive cleanup occurred"); require(await engine.RecoverAsync(contextAvailable: _ => true) == 1 && !File.Exists(file), "Cleanup did not resume on correct drive");
        });
        await check("recovery preserves unowned candidate files and corrupt journals", async () =>
        {
            var (a, b, data) = Setup(); var file = Path.Combine(b, ".sinh-user.tmp"); await File.WriteAllTextAsync(file, "not created by SINH"); var folder = Path.Combine(data, "journal", "active"); Directory.CreateDirectory(folder); await File.WriteAllTextAsync(Path.Combine(folder, "bad.json"), "{invalid"); await File.WriteAllTextAsync(Path.Combine(folder, "unowned.json"), JsonSerializer.Serialize(new JournalState(Guid.NewGuid(), a, b, "Применение", 0, 1, file, TemporaryOwned: false))); var engine = new SyncExecutor(new(data)); await engine.RecoverAsync(); require(await File.ReadAllTextAsync(file) == "not created by SINH", "Unowned candidate deleted"); require(engine.RecoveryWarnings.Count > 0 && Directory.GetFiles(folder, "bad.json.corrupt-*").Length == 1, "Corrupt journal not preserved");
        });
        await check("filesystem size/name limits stop before modifying destination", async () =>
        {
            var (a, b, data) = Setup(); await File.WriteAllTextAsync(Path.Combine(a, "long-name.txt"), "new contents"); await File.WriteAllTextAsync(Path.Combine(b, "long-name.txt"), "old"); var plan = await new ComparisonService().CompareAsync(new(Guid.NewGuid(), a, b)); var engine = new SyncExecutor(new(data)); var rejected = 0;
            foreach (var options in new[] { new ExecutionOptions(MaxFileBytes: 3), new ExecutionOptions(MaxComponentLength: 4), new ExecutionOptions(DestinationReadOnly: true) }) { try { await engine.ApplyAsync(plan, options); } catch (IOException) { rejected++; } }
            require(rejected == 3 && await File.ReadAllTextAsync(Path.Combine(b, "long-name.txt")) == "old", "Filesystem limit damaged destination");
        });
        await check("history checks device identity, corrupted bytes and metadata", async () =>
        {
            var (a, b, data) = Setup(); var file = Path.Combine(a, "версия English.txt"); await File.WriteAllTextAsync(file, "version one"); var version = await TreeScanner.ReadFileAsync(file, Path.GetFileName(file), default); var history = new HistoryStore(data); var id = Guid.NewGuid(); var entry = await history.ArchiveAsync(id, file, version, 100000, default, "volume:test"); await File.WriteAllTextAsync(file, "current");
            var rejected = 0;
            try { await history.RestoreAsync(entry, [a], 100000); } catch (IOException) { rejected++; }
            try { await history.RestoreAsync(entry, [a], 100000, contextAvailable: () => false); } catch (IOException) { rejected++; }
            await File.WriteAllTextAsync(entry.ArchivePath, "corrupted");
            try { await history.RestoreAsync(entry, [a], 100000, contextAvailable: () => true); } catch (IOException) { rejected++; }
            require(rejected == 3 && await File.ReadAllTextAsync(file) == "current", "Invalid history or wrong volume overwrote current file");
            await File.WriteAllTextAsync(Path.Combine(history.Root, id.ToString("N"), "invalid.json"), "{bad");
            require((await history.ListAsync(id)).Count == 1 && history.Warnings.Count == 1, "One corrupt metadata entry hid all history");
            var forged = entry with { ArchivePath = Path.Combine(b, "unrelated.data") }; await File.WriteAllTextAsync(forged.ArchivePath, "unrelated");
            try { await history.DeleteAsync(forged); throw new Exception("Forged archive accepted"); } catch (IOException) { }
            require(File.Exists(forged.ArchivePath), "History cleanup deleted unrelated file"); await history.DeleteAsync(entry); require(!File.Exists(entry.ArchivePath), "Selected history not deleted");
        });
        await check("configuration export roundtrip and safe import reset automation", async () =>
        {
            var path = Path.Combine(temp, "export-" + Guid.NewGuid() + ".json"); var original = new AppSettings { AutoStart = true, ConfigurationBackupDays = 1, Pairs = [new() { Name = "Русский English", Source = "A", Destination = "B", AllowDeletes = true, AutoOnConnect = true, WatchChanges = true, IntervalMinutes = 1, ApprovedRoots = "approved", UsbId = Guid.NewGuid(), SourceVolumeId = Guid.NewGuid() }] };
            await ConfigurationTransfer.ExportAsync(original, path); var imported = await ConfigurationTransfer.ReadImportAsync(path); var pair = imported.Pairs.Single();
            require(!imported.AutoStart && imported.ConfigurationBackupDays == 0 && pair.Name == "Русский English" && pair.Paused && !pair.AllowDeletes && !pair.WatchChanges && !pair.AutoOnConnect && pair.IntervalMinutes == 0 && pair.ApprovedRoots == null && pair.UsbId == null && pair.SourceVolumeId == null && pair.Id != original.Pairs[0].Id, "Imported config enabled unattended mutation");
            require(original.AutoStart && original.Pairs[0].AllowDeletes, "Export mutated active settings"); await File.WriteAllTextAsync(path, "{\"SchemaVersion\":999}"); try { await ConfigurationTransfer.ReadImportAsync(path); throw new Exception("Future schema accepted"); } catch (IOException) { }
        });
    }
}
