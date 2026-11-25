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
    public AsyncCommand CopyError { get; }
    public Func<PairViewModel, bool, Task<bool>>? RunPair { get; set; }
    public Action? CancelRun { get; set; }
    private bool batchActive, stopBatch;
    private TaskCompletionSource? batchCompletion;
    public bool BatchActive => Volatile.Read(ref batchActive);
    public Task BatchCompletion => batchCompletion?.Task ?? Task.CompletedTask;
    public string SyncCaption => Localizer.Format("SyncSelection", Pairs.Count(p => !p.Pair.Paused));
    public void RequestStop() { stopBatch = true; CancelRun?.Invoke(); }
    private bool busy;
    public bool Busy { get => Volatile.Read(ref busy); set { Set(ref busy, value); RefreshCommands(); } }
    private bool dialogOpen;
    public bool DialogOpen => Volatile.Read(ref dialogOpen);
    public Guid? ActivePairId { get; set; }
    public void RefreshCommands() { Add.Refresh(); Edit.Refresh(); Pause.Refresh(); Compare.Refresh(); Sync.Refresh(); Stop.Refresh(); History.Refresh(); Settings.Refresh(); }
    private string status = "Создайте пару папок. Новые пары запускаются вручную.";
    private object?[] statusArgs = [];
    private Exception? error;
    public bool HasError => error != null;
    public string? ErrorDetails { get; private set; }
    public string Status { get => error != null ? Localizer.Error(error) : Localizer.Format(status, statusArgs.ToArray()); set => SetStatus(value); }
    private void RefreshError() { Raise(nameof(HasError)); CopyError.Refresh(); Raise(nameof(Status)); }
    public void SetStatus(string key, params object?[] args) { error = null; ErrorDetails = null; status = key; statusArgs = args; RefreshError(); }
    public void SetError(Exception value, string? details = null) { error = value; ErrorDetails = details ?? value.ToString(); RefreshError(); }
    private Sinh.Core.SyncProgress progress = new("", "");
    public double ProgressValue => progress.Total > 0 ? Math.Clamp(progress.Completed * 100d / progress.Total, 0, 100) : 0;
    public bool ProgressIndeterminate => progress.Total <= 0 && WindowMotion.Enabled;
    internal void RefreshMotion() => Raise(nameof(ProgressIndeterminate));
    public string ProgressInfo => progress.Stage == "Применение" ? Localizer.Format("ProgressOperations", progress.Completed, progress.Total) : progress.Total > 0 ? Localizer.Format("ProgressBytes", progress.Completed / 1048576d, progress.Total / 1048576d) : Localizer.Format("ProgressScan", progress.Completed / 1048576d);
    public void ReportProgress(Sinh.Core.SyncProgress value) { progress = value; SetStatus("StagePath", value.Stage, value.Path); Raise(nameof(ProgressValue)); Raise(nameof(ProgressIndeterminate)); Raise(nameof(ProgressInfo)); }
    public MainViewModel(SettingsStore store)
    {
        Store = store;
        CopyError = new(() => { System.Windows.Clipboard.SetText(ErrorDetails ?? Status); return Task.CompletedTask; }, () => HasError);
        Add = new(() => EditPair(null), () => !Busy && !BatchActive && !DialogOpen);
        Edit = new(() => EditPair(Selected?.Pair), () => Selected != null && !Busy && !BatchActive && !DialogOpen);
        Pause = new(async () => { var id = Selected!.Pair.Id; if (ActivePairId == id) CancelRun?.Invoke(); await Store.UpdateAsync(s => s with { Pairs = s.Pairs.Select(p => p.Id == id ? p with { Paused = !p.Paused } : p).ToArray() }); }, () => Selected != null && !DialogOpen && (!Busy || ActivePairId == Selected.Pair.Id));
        Compare = new(async () => { if (RunPair != null) await RunPair(Selected!, false); }, () => Selected != null && RunPair != null && !Busy && !BatchActive && !DialogOpen);
        Sync = new(RunSelectionAsync, () => RunPair != null && Pairs.Any(p => !p.Pair.Paused) && !Busy && !BatchActive && !DialogOpen);
        Stop = new(() => { RequestStop(); return Task.CompletedTask; }, () => Busy || BatchActive);
        Settings = new(() => { ShowModal(new SettingsWindow(Store, () => !Busy && !BatchActive)); return Task.CompletedTask; }, () => !DialogOpen && !BatchActive);
        History = new(() => { ShowModal(new HistoryWindow(Selected!.Pair, Store.Current.HistoryLimitMiB * 1048576)); return Task.CompletedTask; }, () => Selected != null && !Busy && !BatchActive && !DialogOpen);
        Store.Changed += OnChanged;
        Localizer.Current.PropertyChanged += LocaleChanged;
        Load(store.Current);
    }
    private void OnChanged(object? sender, AppSettings s) => System.Windows.Application.Current.Dispatcher.InvokeAsync(() => Load(Store.Current));
    private void LocaleChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) { Raise(nameof(Status)); Raise(nameof(ProgressInfo)); Raise(nameof(SyncCaption)); foreach (var pair in Pairs) pair.RefreshLanguage(); }
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
            if (row == null) Pairs.Insert(index, new(p, () => TogglePairAsync(p.Id)));
            else
            {
                if (!ReferenceEquals(row.Pair, p)) row.Update(p);
                if (Pairs[index] != row) Pairs.Move(Pairs.IndexOf(row), index);
            }
        }
        Selected = Pairs.FirstOrDefault(p => p.Pair.Id == id) ?? Pairs.FirstOrDefault();
        Raise(nameof(SyncCaption)); RefreshCommands();
    }
    private bool? ShowModal(Window window)
    {
        dialogOpen = true; RefreshCommands();
        try { window.Owner = System.Windows.Application.Current.MainWindow; return window.ShowDialog(); }
        finally { dialogOpen = false; RefreshCommands(); }
    }
    internal async Task RunSelectionAsync()
    {
        if (Busy || BatchActive || DialogOpen || RunPair == null) return;
        var selectedIds = Pairs.Where(p => !p.Pair.Paused).Select(p => p.Pair.Id).ToArray();
        batchActive = true; stopBatch = false; batchCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously); RefreshCommands();
        try
        {
            foreach (var id in selectedIds)
            {
                if (stopBatch) break;
                var row = Pairs.FirstOrDefault(p => p.Pair.Id == id);
                if (row == null || Store.Current.Pairs.FirstOrDefault(p => p.Id == id)?.Paused != false) continue;
                if (!await RunPair(row, true)) break;
            }
        }
        finally { batchActive = false; batchCompletion.TrySetResult(); RefreshCommands(); }
    }
    private async Task TogglePairAsync(Guid id)
    {
        try
        {
            if (ActivePairId == id) CancelRun?.Invoke();
            await Store.UpdateAsync(s => s with { Pairs = s.Pairs.Select(p => p.Id == id ? p with { Paused = !p.Paused } : p).ToArray() });
            Load(Store.Current);
        }
        finally { Pairs.FirstOrDefault(p => p.Pair.Id == id)?.RefreshSelection(); }
    }
    private async Task EditPair(FolderPair? pair)
    {
        dialogOpen = true; RefreshCommands();
        try { await FolderPairEditor.EditAsync(System.Windows.Application.Current.MainWindow, Store, pair); }
        finally { dialogOpen = false; RefreshCommands(); }
    }
    public void Dispose() { Store.Changed -= OnChanged; Localizer.Current.PropertyChanged -= LocaleChanged; }
}
