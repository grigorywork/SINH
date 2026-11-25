using System.Windows;
namespace Sinh.App;

public partial class MainWindow : Window
{
    private readonly SyncCoordinator coordinator;
    private readonly BackgroundScheduler scheduler;
    public MainWindow(MainViewModel model)
    {
        InitializeComponent(); DataContext = model; coordinator = new SyncCoordinator(model);
        LayoutUpdated += FitListViewport;
        scheduler = new BackgroundScheduler(model.Store, pair => Dispatcher.InvokeAsync(() => coordinator.RunAutomaticAsync(pair)).Task.Unwrap(), () => model.Busy || model.DialogOpen);
        Closing += async (_, e) =>
        {
            var app = (App)System.Windows.Application.Current;
            if (!app.ExitRequested && model.Store.Current.CloseBehavior == Sinh.Settings.CloseBehavior.Tray) { e.Cancel = true; Hide(); return; }
            if (!model.Busy && !model.Store.HasPendingSaves) return;
            e.Cancel = true; model.CancelRun?.Invoke();
            await coordinator.Completion;
            try { await model.Store.FlushAsync(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
            Close();
        };
        Closed += (_, _) => { LayoutUpdated -= FitListViewport; scheduler.Dispose(); coordinator.Dispose(); model.Dispose(); };
    }
    private void FitListViewport(object? sender, EventArgs args)
    {
        if (!IsLoaded || LayoutViewport.ViewportHeight <= 0) return;
        var surrounding = MainGrid.DesiredSize.Height - PairsList.DesiredSize.Height;
        var height = Math.Max(160, Math.Floor(LayoutViewport.ViewportHeight - surrounding - 1));
        if (Math.Abs(PairsList.Height - height) > 1) PairsList.Height = height;
    }
}
