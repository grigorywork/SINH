using System.Windows;
namespace Sinh.App;
public partial class MainWindow : Window
{
    private readonly SyncCoordinator coordinator;
    public MainWindow(MainViewModel model)
    {
        InitializeComponent(); DataContext = model; coordinator = new SyncCoordinator(model);
        Closed += (_, _) => { coordinator.Dispose(); model.Dispose(); };
    }
}
