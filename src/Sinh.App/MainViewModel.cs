using System.Collections.ObjectModel;
using System.Windows;
using Sinh.Settings;

namespace Sinh.App;

public sealed class PairViewModel(FolderPair pair) : Observable
{
    public FolderPair Pair { get; } = pair;
    public string Name => Pair.Name;
    public string Paths => Pair.Source + "\n↓\n" + Pair.Destination;
    public string Mode => Pair.Mode switch { SyncMode.Mirror => "Зеркало A → B", SyncMode.TwoWay => "Двусторонний обмен", _ => "Копирование обновлений A → B" };
    public string Last => Pair.LastSuccess?.ToLocalTime().ToString("g") ?? "Ещё не запускалась";
    private string status = pair.Paused ? "На паузе" : "Ручной запуск";
    public string Status { get => status; set => Set(ref status, value); }
}

public sealed class MainViewModel : Observable, IDisposable
{
    internal readonly SettingsStore Store;
    public ObservableCollection<PairViewModel> Pairs { get; } = [];
    private PairViewModel? selected;
    public PairViewModel? Selected { get => selected; set { Set(ref selected, value); Edit.Refresh(); Pause.Refresh(); Compare.Refresh(); Sync.Refresh(); } }
    public AsyncCommand Add { get; }
    public AsyncCommand Edit { get; }
    public AsyncCommand Pause { get; }
    public AsyncCommand Compare { get; }
    public AsyncCommand Sync { get; }
    public AsyncCommand Stop { get; }
    public AsyncCommand Settings { get; }
    public Func<PairViewModel, bool, Task>? RunPair { get; set; }
    public Action? CancelRun { get; set; }
    private string status = "SINH 0.1 · Создайте пару папок. Новые пары запускаются вручную.";
    public string Status { get => status; set => Set(ref status, value); }
    public MainViewModel(SettingsStore store)
    {
        Store = store;
        Add = new(() => EditPair(null));
        Edit = new(() => EditPair(Selected?.Pair), () => Selected != null);
        Pause = new(async () => { var id = Selected!.Pair.Id; await Store.UpdateAsync(s => s with { Pairs = s.Pairs.Select(p => p.Id == id ? p with { Paused = !p.Paused } : p).ToArray() }); }, () => Selected != null);
        Compare = new(async () => { if (RunPair != null) await RunPair(Selected!, false); }, () => Selected != null && RunPair != null);
        Sync = new(async () => { if (RunPair != null) await RunPair(Selected!, true); }, () => Selected != null && RunPair != null);
        Stop = new(() => { CancelRun?.Invoke(); return Task.CompletedTask; }, () => CancelRun != null);
        Settings = new(() => { new SettingsWindow(Store) { Owner = System.Windows.Application.Current.MainWindow }.ShowDialog(); return Task.CompletedTask; });
        Store.Changed += OnChanged;
        Load(store.Current);
    }
    private void OnChanged(object? sender, AppSettings s) => System.Windows.Application.Current.Dispatcher.InvokeAsync(() => Load(s));
    private void Load(AppSettings s)
    {
        var id = Selected?.Pair.Id;
        // Appearance changes do not rebuild pairs or schedule scans.
        if (Pairs.Select(p => p.Pair).SequenceEqual(s.Pairs)) return;
        Pairs.Clear(); foreach (var p in s.Pairs) Pairs.Add(new(p));
        Selected = Pairs.FirstOrDefault(p => p.Pair.Id == id) ?? Pairs.FirstOrDefault();
    }
    private async Task EditPair(FolderPair? pair)
    {
        var dialog = new PairWindow(pair) { Owner = System.Windows.Application.Current.MainWindow };
        if (dialog.ShowDialog() != true || dialog.Result is not { } result) return;
        await Store.UpdateAsync(s => s with { Pairs = pair == null ? [.. s.Pairs, result] : s.Pairs.Select(p => p.Id == result.Id ? result : p).ToArray() });
    }
    public void Dispose() => Store.Changed -= OnChanged;
}
