using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using Sinh.App;
using Sinh.Settings;

internal static class PresentationChecks
{
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static async Task Layout(Window window) { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout(); }
    internal static async Task RunAsync(SettingsStore store, MainViewModel model, Window window, string directory)
    {
        var sources = new FolderSources(Path.Combine(directory, "first", "A"), true);
        sources.AddPaths([Path.Combine(directory, "second", "B")]);
        Program.Children<CheckBox>(sources).First().IsChecked = false;
        sources.AddPaths([Path.Combine(directory, "third", "C")]);
        Require(sources.Grouped && sources.SelectedPaths.SequenceEqual(new[] { Path.Combine(directory, "second", "B"), Path.Combine(directory, "third", "C") }), "Adding folders from different locations reset unchecked choices");
        var target = Path.Combine(directory, "USB копии " + new string('Ж', 60));
        var pairs = Enumerable.Range(0, 6).Select(i => new FolderPair
        {
            Name = "Папка " + i, Source = Path.Combine(directory, "Место " + i, "Папка " + i), Destination = Path.Combine(target, "Папка " + i),
            Paused = i == 5, Mode = i == 0 ? SyncMode.Mirror : i == 1 ? SyncMode.TwoWay : SyncMode.Updates
        }).ToArray();
        foreach (var pair in pairs)
        {
            Directory.CreateDirectory(pair.Source); Directory.CreateDirectory(pair.Destination);
            await File.WriteAllTextAsync(Path.Combine(pair.Source, "keep.txt"), "source"); await File.WriteAllTextAsync(Path.Combine(pair.Destination, "keep.txt"), "copy");
        }
        await store.UpdateAsync(s => s with { Pairs = pairs, CompactPairs = true, GroupPairs = true }); await Layout(window);
        Require(model.List.View.Groups?.Count == 1 && ((CollectionViewGroup)model.List.View.Groups[0]).ItemCount == 6, "Six common destinations did not form one group");
        var group = model.Pairs[0].FolderGroup;
        Require(group.IsGroup && !group.Expanded && model.Selected == null, "Group is not initially collapsed or hidden row remains selected");
        Program.Capture(window, "folders-collapsed");
        var list = (ListBox)window.FindName("PairsList");
        var header = Program.Children<Expander>(list).Single(e => e.DataContext is CollectionViewGroup);
        Require(((FrameworkElement)header.Header).ActualWidth <= list.ActualWidth, "Long destination header exceeds the viewport");
        foreach (var theme in new[] { Theme.Light, Theme.Dark })
        {
            await store.UpdateAsync(s => s with { Appearance = s.Appearance with { Theme = theme } }); await Layout(window);
            var caption = Program.Children<TextBlock>(header).First(t => t.Text == group.Path);
            Require(((SolidColorBrush)caption.Foreground).Color == ((SolidColorBrush)window.FindResource("Ink")).Color, "Group heading lost readable theme foreground");
        }
        Program.Capture(window, "folders-collapsed");
        group.Expanded = true; await Layout(window); model.Selected = model.Pairs[0];
        var compact = Program.Children<ListBoxItem>(list).First(e => e.DataContext == model.Pairs[0]).ActualHeight;
        Require(compact >= 40, "Compact view shrank touch targets");
        var profiles = store.Current.Pairs;
        await store.UpdateAsync(s => s with { CompactPairs = false }); await Layout(window);
        var detailed = Program.Children<ListBoxItem>(list).First(e => e.DataContext == model.Pairs[0]).ActualHeight;
        Require(detailed > compact + 20 && ReferenceEquals(profiles, store.Current.Pairs) && ReferenceEquals(group, model.Pairs[0].FolderGroup) && group.Expanded, "Density did not shrink rows or changed profiles/expansion");
        await store.UpdateAsync(s => s with { CompactPairs = true }); await Layout(window); Program.Capture(window, "folders-compact-expanded");
        group.Expanded = false; Require(model.Selected == null, "Collapsing a group left hidden deletion target selected");
        model.Pairs[0].Status = "Ошибка";
        Require(group.Summary.Contains(Localizer.Format("GroupErrors", 1)), "Collapsed group hides errors");
        model.Pairs[0].Status = "Готово";
        model.Pairs[0].Toggle.Execute(null); await store.FlushAsync(); await Layout(window);
        Require(!group.Expanded && store.Current.Pairs[0].Paused && !store.Current.Pairs[1].Paused, "Unchecking changed another profile or expanded the group");
        using (var runner = new MainViewModel(store))
        {
            var visited = new List<Guid>(); runner.Selected = runner.Pairs[1];
            runner.RunPair = (row, _) => { Require(!runner.Remove.CanExecute(null), "Removal allowed during batch"); visited.Add(row.Pair.Id); return Task.FromResult(true); };
            await runner.RunSelectionAsync();
            Require(visited.SequenceEqual(pairs.Where((_, i) => i != 0 && i != 5).Select(p => p.Id)), "Collapsed groups changed checked batch execution");
        }
        group.Expanded = true; await Layout(window); model.Selected = model.Pairs[1];
        Require(model.Remove.CanExecute(null), "Main removal action missing"); model.Busy = true;
        Require(!model.Remove.CanExecute(null), "Removal allowed during synchronization"); model.Busy = false;
        await FolderPairRemoval.RemoveConfirmedAsync(store, pairs[1].Id); await Layout(window);
        Require(model.Pairs.Count == 5 && model.Pairs.All(p => p.Pair.Id != pairs[1].Id), "Confirmed removal did not update the list");
        foreach (var pair in pairs) Require(await File.ReadAllTextAsync(Path.Combine(pair.Source, "keep.txt")) == "source" && await File.ReadAllTextAsync(Path.Combine(pair.Destination, "keep.txt")) == "copy", "Removing profile touched synchronized files");
        await store.UpdateAsync(s => s with { GroupPairs = false, CompactPairs = false }); await Layout(window);
        using (var recreated = new MainViewModel(store)) Require(!recreated.List.Grouped && !recreated.List.Compact && recreated.Pairs.Count == 5, "Preferences lost after model recreation");
        await store.UpdateAsync(s => s with { Pairs = [], CompactPairs = true, GroupPairs = true }); await Layout(window);
        Console.WriteLine("PASS six-folder collapse, compact/detailed density, long header, live errors/checks, hidden selection, batch and removal without file deletion");
    }
}
