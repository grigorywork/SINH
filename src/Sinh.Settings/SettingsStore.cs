using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sinh.Settings;

public sealed class SettingsStore : IDisposable
{
    private readonly string path;
    private readonly SemaphoreSlim gate = new(1);
    private AppSettings current = new();
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
    public async Task LoadAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
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
                var recovery = path + ".recovery-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
                File.Copy(path, recovery, false);
                RecoveryMessage = "Повреждённые или более новые настройки сохранены: " + recovery;
                Volatile.Write(ref current, new());
            }
        }
        finally { gate.Release(); }
        Changed?.Invoke(this, Current);
    }
    public async Task UpdateAsync(Func<AppSettings, AppSettings> update)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        AppSettings next;
        try
        {
            next = Validate(update(Current));
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
                if (File.Exists(path)) File.Copy(path, path + ".bak", true);
                File.Move(temp, path, true);
                Volatile.Write(ref current, next);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        finally { gate.Release(); }
        Changed?.Invoke(this, next);
    }
    private static AppSettings Validate(AppSettings x)
    {
        var a = x.Appearance ?? new();
        return x with
        {
            SchemaVersion = AppSettings.CurrentSchema,
            Appearance = a with
            {
                Language = a.Language == "en" ? "en" : "ru",
                Scale = a.Scale is .85 or 1 or 1.15 ? a.Scale : 1,
                Font = string.IsNullOrWhiteSpace(a.Font) ? "Segoe UI" : a.Font,
                Transparency = double.IsFinite(a.Transparency) ? Math.Clamp(a.Transparency, 0, 100) : 50,
                Frost = double.IsFinite(a.Frost) ? Math.Clamp(a.Frost, 0, 100) : 0
            },
            Pairs = (x.Pairs ?? []).Where(p => p != null && p.Id != Guid.Empty).DistinctBy(p => p.Id).Select(p => p with { Exclusions = p.Exclusions ?? [], IntervalMinutes = Math.Clamp(p.IntervalMinutes, 0, 10080) }).ToArray(),
            MaxParallelFiles = 1, // This release applies operations sequentially.
            SpeedLimitMiB = Math.Clamp(x.SpeedLimitMiB, 0, 10240),
            ScanMinutes = Math.Clamp(x.ScanMinutes, 1, 1440), LogDays = Math.Clamp(x.LogDays, 1, 3650),
            HistoryDays = Math.Clamp(x.HistoryDays, 1, 3650), HistoryLimitMiB = Math.Clamp(x.HistoryLimitMiB, 16, 1048576)
        };
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
