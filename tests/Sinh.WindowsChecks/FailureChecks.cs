using System.IO;
using System.Text.Json;
using Sinh.App;
using Sinh.Core;
using Sinh.Settings;

internal static class FailureChecks
{
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    internal static async Task RunAsync(MainViewModel model, string directory)
    {
        var source = Path.Combine(directory, "Диагностика", "Wallpapers"); var target = Path.Combine(directory, "Обои");
        Directory.CreateDirectory(source); Directory.CreateDirectory(target);
        var pair = new FolderPair { Source = source, Destination = target, Mode = Sinh.Settings.SyncMode.TwoWay };
        // Simulate the original lookup failing while a subsequent metadata probe sees the folder again.
        var error = new RootAccessException(source, RootFailure.Missing, new DirectoryNotFoundException("Root lookup failed"));
        var report = await SyncFailureReport.CaptureAsync(Path.Combine(directory, "diagnostics"), pair, pair, "Сравнение", error, 30);
        using (var parsed = JsonDocument.Parse(report))
        {
            Require(parsed.RootElement.GetProperty("Error").GetString()!.Contains("Root lookup failed"), "Native error/stack chain lost");
            Require(parsed.RootElement.GetProperty("Roots")[0].GetProperty("Attributes").GetString()!.Contains("Directory"), "Post-failure root metadata not captured");
        }
        Require(Directory.GetFiles(Path.Combine(directory, "diagnostics", "errors"), "*.json").Length == 1, "Local error report missing");
        var blocked = Path.Combine(directory, "blocked-log"); await File.WriteAllTextAsync(blocked, "not a directory");
        var fallback = await SyncFailureReport.CaptureAsync(blocked, pair, pair, "Сравнение", error, 30);
        Require(fallback.Contains("Root lookup failed") && fallback.Contains("Diagnostic log:"), "Log failure hid the original error");
        model.SetError(error, report); Require(model.HasError && model.CopyError.CanExecute(null), "Error-copy action missing");
        model.CopyError.Execute(null); Require(System.Windows.Clipboard.GetText() == report, "Error-copy action lost diagnostic data");
        model.SetStatus("Готово"); Require(!model.HasError && !model.CopyError.CanExecute(null) && model.ErrorDetails == null, "Success retained stale diagnostic state");
        Console.WriteLine("PASS precise root failure metadata, local report/fallback and copy-error lifecycle");
    }
}
