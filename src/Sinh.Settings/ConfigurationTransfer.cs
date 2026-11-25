using System.Text.Json;

namespace Sinh.Settings;

/// <summary>Configuration backups are separate from synchronized-file history. No credentials are stored in this schema.</summary>
public static class ConfigurationTransfer
{
    public static Task ExportAsync(AppSettings snapshot, string path, CancellationToken token = default) => Task.Run(async () =>
    {
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var temporary = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(file, snapshot, SettingsStore.Json, token).ConfigureAwait(false);
                await file.FlushAsync(token).ConfigureAwait(false); file.Flush(true);
            }
            token.ThrowIfCancellationRequested(); File.Move(temporary, full, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }, token);

    public static Task<AppSettings> ReadImportAsync(string path, CancellationToken token = default) => Task.Run(async () =>
    {
        await using var file = File.OpenRead(path);
        if (file.Length > 16 * 1024 * 1024) throw new IOException("Файл настроек слишком большой.");
        var imported = await JsonSerializer.DeserializeAsync<AppSettings>(file, SettingsStore.Json, token).ConfigureAwait(false);
        if (imported == null || imported.SchemaVersion > AppSettings.CurrentSchema) throw new IOException("Версия настроек не поддерживается.");
        // A different computer/volume must be reviewed manually; import cannot enable unattended writes.
        return imported with
        {
            AutoStart = false, StartMinimized = false, ConfigurationBackupDays = 0,
            Pairs = (imported.Pairs ?? []).Where(p => p != null).Select(p => p with
            {
                Id = Guid.NewGuid(), Paused = true, ApprovedRoots = null, LastSuccess = null,
                AutoOnConnect = false, WatchChanges = false, IntervalMinutes = 0, AllowDeletes = false,
                SourceVolumeId = null, SourceRelativePath = null, UsbId = null, UsbRelativePath = null
            }).ToArray()
        };
    }, token);
}
