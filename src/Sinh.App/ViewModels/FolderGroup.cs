namespace Sinh.App;

public sealed class FolderGroup(string identity, bool isGroup, Action<FolderGroup>? collapsed = null) : Observable
{
    public string Identity { get; } = identity;
    public bool IsGroup { get; } = isGroup;
    private bool expanded;
    public bool Expanded { get => expanded; set { if (Set(ref expanded, value) && !value) collapsed?.Invoke(this); } }
    public string Path { get; private set; } = "";
    private IReadOnlyList<PairViewModel> members = [];
    public string Summary => Localizer.Format("GroupIncluded", members.Count(p => p.Included), members.Count)
        + (members.Any(p => p.IsRunning) ? " · " + Localizer.T("Синхронизация…") : "")
        + (members.Any(p => p.HasError) ? " · " + Localizer.Format("GroupErrors", members.Count(p => p.HasError)) : "");
    internal void Update(IReadOnlyList<PairViewModel> rows)
    {
        members = rows; Path = rows.Count == 0 ? "" : rows[0].GroupPath;
        Refresh();
    }
    internal void Refresh() { Raise(nameof(Path)); Raise(nameof(Summary)); }
}
