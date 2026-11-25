using System.IO;
using System.Text.Json;
using Sinh.Settings;

namespace Sinh.App;

/// <summary>Local failure diagnostics: metadata only, no file contents or automatic uploads.</summary>
internal static class SyncFailureReport
{
    internal sealed record Probe(string Path, string? Attributes, string? Error, int? NativeCode);
    internal sealed record Report(string Version, string OS, DateTimeOffset At, string Stage,
        FolderPair Configured, FolderPair? Resolved, string Error, Probe[] Roots);
    internal static Task<string> CaptureAsync(string directory, FolderPair configured, FolderPair? resolved, string stage, Exception error, int retentionDays) => Task.Run(async () =>
    {
        static Probe Inspect(string path)
        {
            try { return new(path, File.GetAttributes(path).ToString(), null, null); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return new(path, null, ex.Message, ex.HResult & 0xffff); }
        }
        var paths = new[] { configured.Source, configured.Destination, resolved?.Source, resolved?.Destination }.OfType<string>().Distinct(StringComparer.Ordinal).ToArray();
        var report = new Report(App.DisplayVersion, Environment.OSVersion.VersionString, DateTimeOffset.UtcNow, stage, configured, resolved, error.ToString(), paths.Select(Inspect).ToArray());
        var text = JsonSerializer.Serialize(report, SettingsStore.Json);
        try
        {
            var folder = Path.Combine(directory, "errors"); Directory.CreateDirectory(folder);
            await File.WriteAllTextAsync(Path.Combine(folder, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N") + ".json"), text).ConfigureAwait(false);
            var threshold = DateTime.UtcNow.AddDays(-Math.Clamp(retentionDays, 1, 3650));
            var files = new DirectoryInfo(folder).EnumerateFiles("*.json").OrderByDescending(f => f.LastWriteTimeUtc).ToArray();
            foreach (var obsolete in files.Where((file, index) => index >= 100 || file.LastWriteTimeUtc < threshold)) obsolete.Delete();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { text += Environment.NewLine + "Diagnostic log: " + ex.Message; }
        return text;
    });
}
