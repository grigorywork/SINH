using System.IO;
using Sinh.Settings;
using Sinh.Core;

namespace Sinh.App;

internal sealed class BackgroundScheduler : IDisposable
{
    private sealed class Schedule : IDisposable
    {
        internal bool Online;
        internal DateTimeOffset LastAttempt;
        internal long DirtyAt;
        internal string WatchKey = "";
        internal readonly List<FileSystemWatcher> Watchers = [];
        public void Dispose() { foreach (var w in Watchers) w.Dispose(); Watchers.Clear(); }
    }
    private readonly SettingsStore store;
    private readonly Func<FolderPair, Task<bool>> run;
    private readonly Func<bool> isBusy;
    private readonly Dictionary<Guid, Schedule> schedules = [];
    private readonly Timer timer;
    private int polling;
    private volatile bool disposed;
    internal BackgroundScheduler(SettingsStore store, Func<FolderPair, Task<bool>> run, Func<bool> isBusy)
    {
        this.store = store; this.run = run; this.isBusy = isBusy;
        timer = new Timer(async _ => await PollAsync(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
    }
    private async Task PollAsync()
    {
        if (disposed || Interlocked.Exchange(ref polling, 1) != 0) return;
        try
        {
            if (isBusy()) return;
            var settings = store.Current;
            var mounts = settings.Pairs.Any(p => !p.Paused && (p.AutoOnConnect || p.WatchChanges || p.IntervalMinutes > 0)) ? WindowsVolumes.Mounts() : new Dictionary<Guid, string>();
            foreach (var removed in schedules.Keys.Except(settings.Pairs.Select(p => p.Id)).ToArray()) { schedules[removed].Dispose(); schedules.Remove(removed); }
            foreach (var pair in settings.Pairs)
            {
                if (disposed) break;
                if (!schedules.TryGetValue(pair.Id, out var schedule)) schedules[pair.Id] = schedule = new();
                var automated = pair.AutoOnConnect || pair.WatchChanges || pair.IntervalMinutes > 0;
                if (pair.Paused || !automated) { schedule.Dispose(); schedule.WatchKey = ""; continue; }
                var resolved = WindowsVolumes.Resolve(pair, mounts);
                var online = resolved != null && Directory.Exists(resolved.Source) && Directory.Exists(resolved.Destination);
                var connected = online && !schedule.Online; schedule.Online = online;
                if (!online) { schedule.Dispose(); schedule.WatchKey = ""; continue; }
                SetWatchers(schedule, resolved!, pair.WatchChanges);
                if (pair.ApprovedRoots == null) continue; // A new or reconfigured pair must complete one manual run.
                var now = DateTimeOffset.UtcNow;
                var interval = pair.IntervalMinutes > 0 ? pair.IntervalMinutes : settings.ScanMinutes;
                var due = (pair.AutoOnConnect && connected) || now - schedule.LastAttempt >= TimeSpan.FromMinutes(interval) || (Volatile.Read(ref schedule.DirtyAt) > 0 && Environment.TickCount64 - Volatile.Read(ref schedule.DirtyAt) >= 3000);
                if (!due || (!connected && now - schedule.LastAttempt < TimeSpan.FromSeconds(30)) || isBusy()) continue;
                schedule.LastAttempt = now; Interlocked.Exchange(ref schedule.DirtyAt, 0);
                if (!await run(resolved!).ConfigureAwait(false)) break;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { System.Diagnostics.Debug.WriteLine("SINH scheduler: " + ex.Message); }
        catch (OperationCanceledException) when (disposed) { }
        finally { if (disposed) { foreach (var schedule in schedules.Values) schedule.Dispose(); schedules.Clear(); } Interlocked.Exchange(ref polling, 0); }
    }
    private void SetWatchers(Schedule schedule, FolderPair pair, bool enabled)
    {
        var key = enabled ? pair.Source + "\n" + pair.Destination + "\n" + string.Join('\n', pair.Exclusions) : "";
        if (schedule.WatchKey == key) return;
        schedule.Dispose(); schedule.WatchKey = key;
        if (!enabled) return;
        foreach (var path in new[] { pair.Source, pair.Destination })
        {
            var watcher = new FileSystemWatcher(path) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size, InternalBufferSize = 16384 };
            var exclusions = new ExclusionRules(pair.Exclusions);
            void Signal(string? fullPath)
            {
                if (disposed || isBusy()) return;
                try
                {
                    if (fullPath != null && (PathRules.Contains(App.DataDirectory, fullPath) || exclusions.MatchesPathOrParent(Path.GetRelativePath(path, fullPath)))) return;
                    Interlocked.Exchange(ref schedule.DirtyAt, Environment.TickCount64);
                }
                catch (IOException) { Interlocked.Exchange(ref schedule.DirtyAt, Environment.TickCount64); }
            }
            watcher.Changed += (_, e) => Signal(e.FullPath); watcher.Created += (_, e) => Signal(e.FullPath); watcher.Deleted += (_, e) => Signal(e.FullPath); watcher.Renamed += (_, e) => { Signal(e.OldFullPath); Signal(e.FullPath); }; watcher.Error += (_, _) => Signal(null);
            schedule.Watchers.Add(watcher); watcher.EnableRaisingEvents = true;
        }
    }
    public void Dispose()
    {
        disposed = true; timer.Dispose();
        // Timer disposal does not wait for an active callback. Watchers are also disposed when that callback completes.
        if (Interlocked.CompareExchange(ref polling, 1, 0) == 0) { foreach (var schedule in schedules.Values) schedule.Dispose(); schedules.Clear(); Interlocked.Exchange(ref polling, 0); }
    }
}
