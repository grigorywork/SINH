using System.Collections.ObjectModel;
using System.Windows;
using Sinh.Settings;

namespace Sinh.App;

public sealed class PairViewModel(FolderPair pair) : Observable
{
    public FolderPair Pair { get; private set; } = pair;
    public void Update(FolderPair value)
    {
        var wasPaused = Pair.Paused; Pair = value;
        Raise(nameof(Name)); Raise(nameof(Paths)); Raise(nameof(Mode)); Raise(nameof(Last));
        if (value.Paused) Status = "На паузе"; else if (wasPaused) Status = "Готова к запуску";
    }
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
    public void RefreshCommands() { Add.Refresh(); Edit.Refresh(); Pause.Refresh(); Compare.Refresh(); Sync.Refresh(); Stop.Refresh(); History.Refresh(); }
    private string status = "Создайте пару папок. Новые пары запускаются вручную.";
    public string Status { get => status; set => Set(ref status, value); }
    public MainViewModel(SettingsStore store)
    {
        Store = store;
        Add = new(() => EditPair(null), () => !Busy);
        Edit = new(() => EditPair(Selected?.Pair), () => Selected != null && !Busy);
        Pause = new(async () => { var id = Selected!.Pair.Id; if(ActivePairId==id) CancelRun?.Invoke(); await Store.UpdateAsync(s => s with { Pairs = s.Pairs.Select(p => p.Id == id ? p with { Paused = !p.Paused } : p).ToArray() }); }, () => Selected != null && (!Busy || ActivePairId==Selected.Pair.Id));
        Compare = new(async () => { if (RunPair != null) await RunPair(Selected!, false); }, () => Selected != null && RunPair != null && !Busy);
        Sync = new(async () => { if (RunPair != null) await RunPair(Selected!, true); }, () => Selected != null && RunPair != null && !Busy && Selected.Pair.Paused == false);
        Stop = new(() => { CancelRun?.Invoke(); return Task.CompletedTask; }, () => Busy);
        Settings = new(() => { ShowModal(new SettingsWindow(Store)); return Task.CompletedTask; });
        History = new(() => { ShowModal(new HistoryWindow(Selected!.Pair, Store.Current.HistoryLimitMiB * 1048576)); return Task.CompletedTask; }, () => Selected != null && !Busy);
        Store.Changed += OnChanged;
        Load(store.Current);
    }
    private void OnChanged(object? sender, AppSettings s) => System.Windows.Application.Current.Dispatcher.InvokeAsync(() => Load(Store.Current));
    private void Load(AppSettings s)
    {
        var id = Selected?.Pair.Id;
        // Appearance changes do not rebuild pairs or schedule scans.
        if (Pairs.Select(p => p.Pair).SequenceEqual(s.Pairs)) return;
        foreach (var obsolete in Pairs.Where(row=>!s.Pairs.Any(p=>p.Id==row.Pair.Id)).ToArray()) Pairs.Remove(obsolete);
        for(var index=0;index<s.Pairs.Length;index++)
        {
            var p=s.Pairs[index];var row=Pairs.FirstOrDefault(x=>x.Pair.Id==p.Id);
            if(row==null)Pairs.Insert(index,new(p));
            else {row.Update(p);var oldIndex=Pairs.IndexOf(row);if(oldIndex!=index)Pairs.Move(oldIndex,index);}
        }
        Selected = Pairs.FirstOrDefault(p => p.Pair.Id == id) ?? Pairs.FirstOrDefault();
    }
    private bool? ShowModal(Window window)
    {
        dialogOpen=true;
        try {window.Owner=System.Windows.Application.Current.MainWindow;return window.ShowDialog();}
        finally {dialogOpen=false;}
    }
    private async Task EditPair(FolderPair? pair)
    {
        var dialog = new PairWindow(pair);
        if (ShowModal(dialog) != true || dialog.Result is not { } result) return;
        await Task.Run(() =>
        {
            foreach (var other in Store.Current.Pairs.Where(p => p.Id != result.Id))
            {
                if (Sinh.Core.PathRules.Contains(result.Destination, other.Destination) || Sinh.Core.PathRules.Contains(other.Destination, result.Destination) || Sinh.Core.PathRules.Contains(result.Destination, other.Source) || Sinh.Core.PathRules.Contains(other.Source, result.Destination) || Sinh.Core.PathRules.Contains(other.Destination, result.Source) || Sinh.Core.PathRules.Contains(result.Source, other.Destination))
                    throw new System.IO.IOException("Пара пересекается с назначением другой пары: " + other.Name + ". Выберите независимые папки копий.");
            }
        });
        await Store.UpdateAsync(s => s with { Pairs = pair == null ? [.. s.Pairs, result] : s.Pairs.Select(p => p.Id == result.Id ? result : p).ToArray() });
    }
    public void Dispose() => Store.Changed -= OnChanged;
}
