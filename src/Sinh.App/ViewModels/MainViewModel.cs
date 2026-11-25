using System.Collections.ObjectModel;
using System.Windows;
using Sinh.Settings;

namespace Sinh.App;

public sealed class MainViewModel : Observable, IDisposable
{
    internal readonly SettingsStore Store;
    public ObservableCollection<PairViewModel> Pairs { get; } = [];
    private PairViewModel? selected;
    public PairViewModel? Selected { get => selected; set { Set(ref selected, value); RefreshCommands(); } }
    public AsyncCommand Add { get; }
    public AsyncCommand Edit { get; }
    public AsyncCommand Pause { get; }
    public AsyncCommand Compare { get; }
    public AsyncCommand Sync { get; }
    public AsyncCommand Stop { get; }
    public AsyncCommand Settings { get; }
    public AsyncCommand History { get; }
    public Func<PairViewModel, bool, Task>? RunPair { get; set; }
    public Action? CancelRun { get; set; }
    private bool busy;
    public bool Busy { get => Volatile.Read(ref busy); set { Set(ref busy, value); RefreshCommands(); } }
    private bool dialogOpen;
    public bool DialogOpen => Volatile.Read(ref dialogOpen);
    public Guid? ActivePairId { get; set; }
    public void RefreshCommands() { Add.Refresh(); Edit.Refresh(); Pause.Refresh(); Compare.Refresh(); Sync.Refresh(); Stop.Refresh(); History.Refresh(); Settings.Refresh(); }
    private string status = "Создайте пару папок. Новые пары запускаются вручную.";
    private object?[] statusArgs = [];
    private Exception? error;
    public string Status { get => error != null ? Localizer.Error(error) : Localizer.Format(status, statusArgs.ToArray()); set { error = null; statusArgs = []; Set(ref status, value); } }
    public void SetStatus(string key, params object?[] args) { error = null; status = key; statusArgs = args; Raise(nameof(Status)); }
    public void SetError(Exception value) { error = value; Raise(nameof(Status)); }
    private Sinh.Core.SyncProgress progress = new("", "");
    public double ProgressValue => progress.Total > 0 ? Math.Clamp(progress.Completed * 100d / progress.Total, 0, 100) : 0;
    public bool ProgressIndeterminate => progress.Total <= 0 && WindowMotion.Enabled;
    internal void RefreshMotion() => Raise(nameof(ProgressIndeterminate));
    public string ProgressInfo => progress.Stage == "Применение" ? Localizer.Format("ProgressOperations", progress.Completed, progress.Total) : progress.Total > 0 ? Localizer.Format("ProgressBytes", progress.Completed / 1048576d, progress.Total / 1048576d) : Localizer.Format("ProgressScan", progress.Completed / 1048576d);
    public void ReportProgress(Sinh.Core.SyncProgress value) { progress = value; SetStatus("StagePath", value.Stage, value.Path); Raise(nameof(ProgressValue)); Raise(nameof(ProgressIndeterminate)); Raise(nameof(ProgressInfo)); }
    public MainViewModel(SettingsStore store)
    {
        Store = store;
        Add = new(() => EditPair(null), () => !Busy && !DialogOpen);
        Edit = new(() => EditPair(Selected?.Pair), () => Selected != null && !Busy && !DialogOpen);
        Pause = new(async () => { var id = Selected!.Pair.Id; if (ActivePairId == id) CancelRun?.Invoke(); await Store.UpdateAsync(s => s with { Pairs = s.Pairs.Select(p => p.Id == id ? p with { Paused = !p.Paused } : p).ToArray() }); }, () => Selected != null && !DialogOpen && (!Busy || ActivePairId == Selected.Pair.Id));
        Compare = new(async () => { if (RunPair != null) await RunPair(Selected!, false); }, () => Selected != null && RunPair != null && !Busy && !DialogOpen);
        Sync = new(async () => { if (RunPair != null) await RunPair(Selected!, true); }, () => Selected != null && RunPair != null && !Busy && !DialogOpen && Selected.Pair.Paused == false);
        Stop = new(() => { CancelRun?.Invoke(); return Task.CompletedTask; }, () => Busy);
        Settings = new(() => { ShowModal(new SettingsWindow(Store, () => !Busy)); return Task.CompletedTask; }, () => !DialogOpen);
        History = new(() => { ShowModal(new HistoryWindow(Selected!.Pair, Store.Current.HistoryLimitMiB * 1048576)); return Task.CompletedTask; }, () => Selected != null && !Busy && !DialogOpen);
        Store.Changed += OnChanged;
        Localizer.Current.PropertyChanged += LocaleChanged;
        Load(store.Current);
    }
    private void OnChanged(object? sender, AppSettings s) => System.Windows.Application.Current.Dispatcher.InvokeAsync(() => Load(Store.Current));
    private void LocaleChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) { Raise(nameof(Status)); Raise(nameof(ProgressInfo)); foreach (var pair in Pairs) pair.RefreshLanguage(); }
    private void Load(AppSettings s)
    {
        var id = Selected?.Pair.Id;
        // Appearance changes do not rebuild pairs or schedule scans.
        if (Pairs.Select(p => p.Pair).SequenceEqual(s.Pairs)) return;
        var ids = s.Pairs.Select(p => p.Id).ToHashSet();
        foreach (var obsolete in Pairs.Where(row => !ids.Contains(row.Pair.Id)).ToArray()) Pairs.Remove(obsolete);
        var rows = Pairs.ToDictionary(row => row.Pair.Id);
        for (var index = 0; index < s.Pairs.Count; index++)
        {
            var p = s.Pairs[index]; var row = rows.GetValueOrDefault(p.Id);
            if (row == null) Pairs.Insert(index, new(p));
            else
            {
                if (!ReferenceEquals(row.Pair, p)) row.Update(p);
                if (Pairs[index] != row) Pairs.Move(Pairs.IndexOf(row), index);
            }
        }
        Selected = Pairs.FirstOrDefault(p => p.Pair.Id == id) ?? Pairs.FirstOrDefault();
    }
    private bool? ShowModal(Window window)
    {
        dialogOpen = true; RefreshCommands();
        try { window.Owner = System.Windows.Application.Current.MainWindow; return window.ShowDialog(); }
        finally { dialogOpen = false; RefreshCommands(); }
    }
    private async Task EditPair(FolderPair? pair)
    {
        dialogOpen = true; RefreshCommands();
        try
        {
            var dialog = new PairWindow(pair) { Owner = System.Windows.Application.Current.MainWindow };
            if (dialog.ShowDialog() != true || dialog.Result is not { } result) return;
            await Task.Run(() => PairBoundaries.Validate(result, Store.Current.Pairs));
            await Store.UpdateAsync(s => s with { Pairs = pair == null ? [.. s.Pairs, result] : s.Pairs.Select(p => p.Id == result.Id ? result with { Paused = p.Paused } : p).ToArray() });
        }
        finally { dialogOpen = false; RefreshCommands(); }
    }
    public void Dispose() { Store.Changed -= OnChanged; Localizer.Current.PropertyChanged -= LocaleChanged; }
}
