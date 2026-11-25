using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sinh.Settings;

public sealed class SettingsStore : IDisposable
{
    private readonly string path;
    private readonly SemaphoreSlim gate = new(1);
    private readonly object pendingLock = new();
    private readonly List<Task> pending = [];
    private AppSettings current = new();
    private bool preserveBackup;
    public AppSettings Current => Volatile.Read(ref current);
    public string? RecoveryMessage { get; private set; }
    public event EventHandler<AppSettings>? Changed;
    public static readonly JsonSerializerOptions Json = CreateJson();
    public SettingsStore(string path) => this.path = path;
    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true };
        options.Converters.Add(new LenientEnumConverter<Theme>());
        options.Converters.Add(new LenientEnumConverter<IconStyle>());
        options.Converters.Add(new LenientEnumConverter<SyncMode>());
        options.Converters.Add(new LenientEnumConverter<CloseBehavior>());
        return options;
    }
    public Task LoadAsync()=>Task.Run(LoadCoreAsync);
    private async Task LoadCoreAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            RecoveryMessage=null;preserveBackup=false;
            if (!File.Exists(path)) return;
            try
            {
                await using var file = File.OpenRead(path);
                var loaded = await JsonSerializer.DeserializeAsync<AppSettings>(file, Json).ConfigureAwait(false);
                if (loaded == null || loaded.SchemaVersion > AppSettings.CurrentSchema) throw new JsonException("Unsupported schema");
                Volatile.Write(ref current, Validate(loaded));
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException)
            {
                var recovery = path + ".recovery-" + Guid.NewGuid().ToString("N");
                File.Copy(path, recovery, false);
                RecoveryMessage = "Повреждённые или более новые настройки сохранены: " + recovery;
                preserveBackup=true;
                Volatile.Write(ref current, new());
            }
        }
        finally { gate.Release(); }
        Changed?.Invoke(this, Current);
    }
    public bool HasPendingSaves { get { lock (pendingLock) return pending.Any(t => !t.IsCompleted); } }
    public Task FlushAsync() { lock (pendingLock) return Task.WhenAll(pending.ToArray()); }
    public Task UpdateAsync(Func<AppSettings, AppSettings> update)
    {
        // Even small synchronous filesystem operations execute on a worker, never on the WPF caller.
        lock (pendingLock)
        {
            pending.RemoveAll(t => t.IsCompleted);
            var task = Task.Run(() => UpdateCoreAsync(update));
            pending.Add(task);
            return task;
        }
    }
    private async Task UpdateCoreAsync(Func<AppSettings, AppSettings> update)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        AppSettings next;
        try
        {
            var previous=Current;
            next = Validate(update(previous),previous);
            if(next==previous&&File.Exists(path)&&!preserveBackup)return;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await JsonSerializer.SerializeAsync(file, next, Json).ConfigureAwait(false);
                    await file.FlushAsync().ConfigureAwait(false);
                    file.Flush(true);
                }
                if (File.Exists(path)&&!preserveBackup) File.Copy(path, path + ".bak", true);
                File.Move(temp, path, true);
                Volatile.Write(ref current, next);
                preserveBackup=false;
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        finally { gate.Release(); }
        Changed?.Invoke(this, next);
    }
    private static AppSettings Validate(AppSettings x,AppSettings? previous=null)
    {
        var a = x.Appearance ?? new();
        return x with
        {
            SchemaVersion = AppSettings.CurrentSchema,
            Appearance = a with
            {
                Language = a.Language == "en" ? "en" : "ru",
                Scale = a.Scale is .85 or 1 or 1.15 ? a.Scale : 1,
                Font = string.IsNullOrWhiteSpace(a.Font) ? "System" : a.Font,
                Transparency = double.IsFinite(a.Transparency) ? Math.Clamp(a.Transparency, 0, 100) : 50,
                Frost = double.IsFinite(a.Frost) ? Math.Clamp(a.Frost, 0, 100) : 0
            },
            Pairs = FreezePairs(x.Pairs,previous),
            MaxParallelFiles = 1, // This release applies operations sequentially.
            SpeedLimitMiB = Math.Clamp(x.SpeedLimitMiB, 0, 10240),
            ScanMinutes = Math.Clamp(x.ScanMinutes, 1, 1440),
            LogDays = Math.Clamp(x.LogDays, 1, 3650),
            ConfigurationBackupDays = x.ConfigurationBackupDays is 0 or 1 or 3 or 10 or 30 ? x.ConfigurationBackupDays : 0,
            HistoryDays = Math.Clamp(x.HistoryDays, 1, 3650),
            HistoryLimitMiB = Math.Clamp(x.HistoryLimitMiB, 16, 1048576)
        };
    }
    private static IReadOnlyList<FolderPair> FreezePairs(IReadOnlyList<FolderPair>? pairs,AppSettings? previous)
    {
        if(previous!=null&&ReferenceEquals(pairs,previous.Pairs))return previous.Pairs;
        var prior=previous?.Pairs.ToDictionary(p=>p.Id)??[];
        var result=(pairs??[]).Where(p=>p!=null&&p.Id!=Guid.Empty).DistinctBy(p=>p.Id).Select(p=>
        {
            prior.TryGetValue(p.Id,out var old);
            if(ReferenceEquals(p,old))return p;
            var exclusions=old!=null&&ReferenceEquals(p.Exclusions,old.Exclusions)?old.Exclusions:Array.AsReadOnly((p.Exclusions??[]).Where(e=>e!=null).ToArray());
            var valid=!string.IsNullOrWhiteSpace(p.Source)&&!string.IsNullOrWhiteSpace(p.Destination);
            return p with {Name=string.IsNullOrWhiteSpace(p.Name)?"Новая пара":p.Name,Source=p.Source??"",Destination=p.Destination??"",Exclusions=exclusions,IntervalMinutes=Math.Clamp(p.IntervalMinutes,0,10080),Paused=p.Paused||!valid,ApprovedRoots=valid?p.ApprovedRoots:null};
        }).ToArray();
        return Array.AsReadOnly(result);
    }
    public void Dispose() { Changed = null; gate.Dispose(); }
}

internal sealed class LenientEnumConverter<T> : JsonConverter<T> where T : struct, Enum
{
    public override T Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String && Enum.TryParse<T>(reader.GetString(), true, out var value) && Enum.IsDefined(value)) return value;
        if (reader.TokenType == JsonTokenType.Number) { reader.TryGetInt32(out _); return default; }
        if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray) reader.Skip();
        return default;
    }
    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());
}
