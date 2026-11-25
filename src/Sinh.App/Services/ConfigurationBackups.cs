using System.IO;
using Sinh.Settings;

namespace Sinh.App;

internal sealed class ConfigurationBackups : IDisposable
{
    private readonly SettingsStore store;
    private readonly CancellationTokenSource cancellation = new();
    private readonly Task worker;
    internal static string DirectoryPath => Path.Combine(App.DataDirectory, "configuration-backups");
    internal ConfigurationBackups(SettingsStore store) { this.store = store; worker = Task.Run(RunAsync); }
    internal static Task SaveAsync(AppSettings settings, CancellationToken token = default) => ConfigurationTransfer.ExportAsync(settings, Path.Combine(DirectoryPath, "settings-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff") + ".json"), token);
    private async Task RunAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            do
            {
                var days = store.Current.ConfigurationBackupDays;
                if (days == 0) continue;
                try
                {
                    var last = Directory.Exists(DirectoryPath) ? Directory.EnumerateFiles(DirectoryPath, "settings-*.json").Select(File.GetLastWriteTimeUtc).DefaultIfEmpty(DateTime.MinValue).Max() : DateTime.MinValue;
                    if (DateTime.UtcNow - last >= TimeSpan.FromDays(days)) await SaveAsync(store.Current, cancellation.Token).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { _ = System.Windows.Application.Current.Dispatcher.InvokeAsync(() => ((App)System.Windows.Application.Current).Notify("Ошибка", Localizer.Error(ex), "error")); }
            } while (await timer.WaitForNextTickAsync(cancellation.Token).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
    }
    public void Dispose() { cancellation.Cancel(); _ = worker.ContinueWith(_ => cancellation.Dispose(), TaskScheduler.Default); }
}
