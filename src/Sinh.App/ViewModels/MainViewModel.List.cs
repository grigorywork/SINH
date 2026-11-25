using System.Windows;

namespace Sinh.App;

public sealed partial class MainViewModel
{
    public PairListPresentation List { get; }
    public AsyncCommand ToggleView { get; }
    public AsyncCommand ToggleGrouping { get; }
    public AsyncCommand Remove { get; }
    private async Task ChangeViewAsync(bool grouping)
    {
        await Store.UpdateAsync(s => grouping ? s with { GroupPairs = !s.GroupPairs } : s with { CompactPairs = !s.CompactPairs });
        Load(Store.Current);
    }
    private async Task RemoveSelectedAsync()
    {
        if (Selected is not { } row) return;
        dialogOpen = true; RefreshCommands();
        try { await FolderPairRemoval.RemoveAsync(System.Windows.Application.Current.MainWindow, Store, row.Pair); }
        finally { dialogOpen = false; RefreshCommands(); }
    }
}
