using System.Windows;
namespace Sinh.App;
public partial class MainWindow : Window
{
    private readonly SyncCoordinator coordinator;
    public MainWindow(MainViewModel model)
    {
        InitializeComponent(); DataContext = model; coordinator = new SyncCoordinator(model);
        Closing += async (_, e) =>
        {
            if (!model.Busy) return;
            e.Cancel = true; model.CancelRun?.Invoke();
            await coordinator.Completion; Close();
        };
        Closed += (_, _) => { coordinator.Dispose(); model.Dispose(); };
    }
}
