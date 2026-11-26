using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using Sinh.Settings;

namespace Sinh.App;

/// <summary>Groups existing rows without replacing profiles or triggering filesystem work.</summary>
public sealed class PairListPresentation : Observable, IDisposable
{
    private readonly ObservableCollection<PairViewModel> rows;
    private readonly Action<FolderGroup> collapsed;
    private readonly Dictionary<string, FolderGroup> groups = [];
    private readonly HashSet<PairViewModel> observed = [];
    public ICollectionView View { get; }
    private bool compact = true, grouped;
    public bool Compact => compact;
    public bool Grouped => grouped;
    public string Caption => Localizer.T(compact ? "Вид: компактный" : "Вид: подробный");
    public PairListPresentation(ObservableCollection<PairViewModel> rows, Action<FolderGroup> collapsed)
    {
        this.rows = rows; this.collapsed = collapsed; View = new ListCollectionView(rows);
    }
    internal void Configure(AppSettings settings)
    {
        if (Set(ref compact, settings.CompactPairs, nameof(Compact))) Raise(nameof(Caption));
        if (Set(ref grouped, settings.GroupPairs, nameof(Grouped)))
        {
            using (View.DeferRefresh())
            {
                View.GroupDescriptions.Clear();
                if (grouped) View.GroupDescriptions.Add(new PropertyGroupDescription(nameof(PairViewModel.FolderGroup)));
            }
        }
    }
    internal void Rebuild()
    {
        foreach (var removed in observed.Except(rows).ToArray()) { removed.PropertyChanged -= RowChanged; observed.Remove(removed); }
        foreach (var row in rows.Where(r => !observed.Contains(r))) { row.PropertyChanged += RowChanged; observed.Add(row); }
        var used = new HashSet<string>();
        using (View.DeferRefresh())
        {
            foreach (var cohort in rows.GroupBy(r => FolderGrouping.Describe(r.Pair).Key))
            {
                var members = cohort.ToArray(); var shared = members.Length > 1;
                var key = shared ? cohort.Key : "pair:" + members[0].Pair.Id;
                if (!groups.TryGetValue(key, out var group)) groups[key] = group = new(key, shared, collapsed);
                used.Add(key); group.Update(members);
                foreach (var row in members) row.SetGroup(group);
            }
            foreach (var key in groups.Keys.Except(used).ToArray()) groups.Remove(key);
        }
        View.Refresh();
    }
    private void RowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not PairViewModel row) return;
        if (e.PropertyName == nameof(PairViewModel.GroupPath)) Rebuild();
        else if (e.PropertyName is nameof(PairViewModel.Status) or nameof(PairViewModel.Included)) row.FolderGroup.Refresh();
    }
    internal void RefreshLanguage() { Raise(nameof(Caption)); foreach (var group in groups.Values) group.Refresh(); }
    public void Dispose() { foreach (var row in observed) row.PropertyChanged -= RowChanged; observed.Clear(); groups.Clear(); }
}
