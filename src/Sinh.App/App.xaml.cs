using System.IO;
using System.Windows;
using Sinh.Settings;
using System.Diagnostics;

namespace Sinh.App;

public partial class App : System.Windows.Application
{
    internal static string DisplayVersion => typeof(App).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false).OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion.Split('+')[0] ?? "0.4.0-preview.1";
    internal static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SINH");
    internal SettingsStore Store { get; private set; } = null!;
    private AppearanceService? appearance;
    private LocalizationService? localization;
    private InstanceGuard? instance;
    private TrayService? tray;
    private ConfigurationBackups? configurationBackups;
    internal bool ExitRequested { get; private set; }
    internal void ShowMain() { if (MainWindow == null) return; MainWindow.Show(); MainWindow.WindowState = WindowState.Normal; MainWindow.Activate(); }
    internal void RequestExit() { ExitRequested = true; if (MainWindow != null) MainWindow.Close(); else Shutdown(); }
    internal void Notify(string title, string message, string kind) => tray?.Notify(title, message, kind);
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var handoff = e.Args.FirstOrDefault(a => a.StartsWith("--handoff=", StringComparison.Ordinal));
            if (handoff != null && int.TryParse(handoff[10..], out var previousId))
            {
                try { using var previous = Process.GetProcessById(previousId); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60)); await previous.WaitForExitAsync(timeout.Token); }
                catch (ArgumentException) { }
            }
            instance = new InstanceGuard();
            if (!instance.IsPrimary) { Shutdown(); return; }
            instance.Listen(() => Dispatcher.InvokeAsync(ShowMain));
            Directory.CreateDirectory(DataDirectory);
            NativeIdentity.SetIdentity();
            Store = new SettingsStore(Path.Combine(DataDirectory, "settings.json"));
            await Task.Run(Store.LoadAsync);
            localization = new LocalizationService(Store);
            var recovery = new Sinh.Core.SyncExecutor(new Sinh.Core.StateStore(DataDirectory));
            var recovered = await Task.Run(() => recovery.RecoverAsync(contextAvailable: j => WindowsVolumes.CanRecover(j, Store.Current.Pairs)));
            await recovery.PruneCompletedLogsAsync(Store.Current.LogDays);
            appearance = new AppearanceService(Store);
            var model = new MainViewModel(Store);
            if (recovered > 0) model.Status = $"Незавершённых запусков: {recovered}. Временные файлы убраны; выполните новое сравнение.";
            if (recovery.RecoveryWarnings.Count > 0) model.Status = "Некоторые журналы требуют проверки; они сохранены в служебной папке SINH.";
            var window = new MainWindow(model);
            MainWindow = window;
            tray = new TrayService(Store);
            configurationBackups = new ConfigurationBackups(Store);
            window.Show();
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            if (e.Args.Contains("--background") || (e.Args.Contains("--startup") && Store.Current.StartMinimized)) window.Hide();
            if (Store.RecoveryMessage != null) MessageBox.Show(window, Store.RecoveryMessage, "SINH");
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "SINH — ошибка запуска"); Shutdown(1); }
    }
    protected override void OnExit(ExitEventArgs e) { configurationBackups?.Dispose(); tray?.Dispose(); appearance?.Dispose(); localization?.Dispose(); Store?.Dispose(); instance?.Dispose(); base.OnExit(e); }
}
