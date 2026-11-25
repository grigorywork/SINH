using Sinh.Settings;

namespace Sinh.App;

public sealed class PairViewModel(FolderPair pair, Func<Task>? toggle = null) : Observable
{
    public FolderGroup FolderGroup { get; private set; } = new("pair:" + pair.Id, false);
    internal void SetGroup(FolderGroup group) { FolderGroup = group; Raise(nameof(FolderGroup)); }
    public string GroupPath => FolderGrouping.Parent(effectiveDestination ?? Pair.Destination);
    public string ModeCaption => Localizer.T(Pair.Mode switch { SyncMode.Mirror => "Зеркало", SyncMode.TwoWay => "Обмен", _ => "Обновления" });
    public bool IsRunning => status is "Сравнение…" or "Копирование…";
    public bool HasError => status == "Ошибка";
    public string InlinePaths => (effectiveSource ?? Pair.Source) + (Pair.Mode == SyncMode.TwoWay ? " ↔ " : " → ") + (effectiveDestination ?? Pair.Destination);
    public string Details => Mode + "\n" + Paths;
    public bool Included => !Pair.Paused;
    public AsyncCommand Toggle { get; } = new(toggle ?? (() => Task.CompletedTask));
    public void RefreshSelection() => Raise(nameof(Included));
    public FolderPair Pair { get; private set; } = pair;
    public void Update(FolderPair value)
    {
        var wasPaused = Pair.Paused;
        if (Pair.Source != value.Source || Pair.Destination != value.Destination) effectiveSource = effectiveDestination = null;
        Pair = value;
        RefreshSelection(); Raise(nameof(Name)); Raise(nameof(Paths)); Raise(nameof(InlinePaths)); Raise(nameof(Details)); Raise(nameof(Mode)); Raise(nameof(ModeCaption)); Raise(nameof(Last));
        if (value.Paused) Status = "На паузе"; else if (wasPaused) Status = "Готова к запуску";
    }
    public string Name => Pair.Name;
    private string? effectiveSource, effectiveDestination;
    public string Paths => (effectiveSource ?? Pair.Source) + (Pair.Mode == SyncMode.TwoWay ? "\n↔\n" : "\n↓\n") + (effectiveDestination ?? Pair.Destination);
    public void SetResolvedPaths(string a, string b) { effectiveSource = a; effectiveDestination = b; Raise(nameof(Paths)); Raise(nameof(InlinePaths)); Raise(nameof(Details)); Raise(nameof(GroupPath)); }
    public string Mode => Localizer.T(Pair.Mode switch { SyncMode.Mirror => "Зеркало A → B", SyncMode.TwoWay => "Двусторонний обмен", _ => "Копирование обновлений A → B" });
    public string Last => Pair.LastSuccess?.ToLocalTime().ToString("g") ?? Localizer.T("Ещё не запускалась");
    private string status = pair.Paused ? "На паузе" : "Ручной запуск";
    private object?[] statusArgs = [];
    public string Status { get => Localizer.Format(status, statusArgs.ToArray()); set { statusArgs = []; Set(ref status, value); } }
    public void SetStatus(string key, params object?[] args) { status = key; statusArgs = args; Raise(nameof(Status)); }
    public void RefreshLanguage() { Raise(nameof(Details)); Raise(nameof(Mode)); Raise(nameof(ModeCaption)); Raise(nameof(Last)); Raise(nameof(Status)); }
}

