using System.IO;
using System.Windows;
using Sinh.Settings;

namespace Sinh.App;

public partial class App : System.Windows.Application
{
    internal static string DisplayVersion => typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.2.0";
    internal static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SINH");
    internal SettingsStore Store { get; private set; } = null!;
    private AppearanceService? appearance;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            Directory.CreateDirectory(DataDirectory);
            NativeIdentity.SetIdentity();
            Store = new SettingsStore(Path.Combine(DataDirectory, "settings.json"));
            await Task.Run(Store.LoadAsync);
            var recovered = await Task.Run(() => new Sinh.Core.SyncExecutor(new Sinh.Core.StateStore(DataDirectory)).RecoverAsync());
            appearance = new AppearanceService(Store);
            var model = new MainViewModel(Store);
            if (recovered > 0) model.Status = $"Незавершённых запусков: {recovered}. Временные файлы убраны; выполните новое сравнение.";
            var window = new MainWindow(model);
            MainWindow = window;
            window.Show();
            if (Store.RecoveryMessage != null) MessageBox.Show(window, Store.RecoveryMessage, "SINH");
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "SINH — ошибка запуска"); Shutdown(1); }
    }
    protected override void OnExit(ExitEventArgs e) { appearance?.Dispose(); Store?.Dispose(); base.OnExit(e); }
}
