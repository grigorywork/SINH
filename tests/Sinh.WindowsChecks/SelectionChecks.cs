using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Sinh.App;
using Sinh.Settings;

internal static class SelectionChecks
{
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    internal static async Task RunAsync(SettingsStore store, MainViewModel main, Window owner, string directory)
    {
        // Use a separate view-model with a controlled runner; never touch user paths or start the scheduler.
        var selected = new[] { new FolderPair { Name = "Первая", Source = "A", Destination = "B" }, new FolderPair { Name = "Skipped", Source = "C", Destination = "D", Paused = true }, new FolderPair { Name = "Third", Source = "E", Destination = "F", UsbSide = UsbSide.Source } };
        await store.UpdateAsync(s => s with { Pairs = selected }); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        using var model = new MainViewModel(store);
        var order = new List<Guid>(); var active = 0;
        model.RunPair = async (row, apply) =>
        {
            Require(apply && ++active == 1 && model.BatchActive, "Batch overlapped or lost ownership");
            Require(!model.Add.CanExecute(null) && !model.Sync.CanExecute(null), "Batch allows reentrant edits or runs");
            order.Add(row.Pair.Id); await Task.Delay(5); active--; return true;
        };
        await model.RunSelectionAsync();
        Require(order.SequenceEqual(new[] { selected[0].Id, selected[2].Id }) && !model.BatchActive, "Checked profiles were skipped or unchecked profiles ran");
        Require(main.Pairs.Count == 3 && main.Pairs[0].Included && !main.Pairs[1].Included, "Persisted checks did not reach UI rows");
        order.Clear();
        model.RunPair = (row, _) => { order.Add(row.Pair.Id); model.RequestStop(); return Task.FromResult(true); };
        await model.RunSelectionAsync(); Require(order.Count == 1, "Stop started another profile");
        order.Clear();
        model.RunPair = (row, _) => { order.Add(row.Pair.Id); return Task.FromResult(false); };
        await model.RunSelectionAsync(); Require(order.Count == 1, "Declined/failed profile did not stop batch");
        model.Pairs[0].Toggle.Execute(null); await store.FlushAsync(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Require(store.Current.Pairs[0].Paused && !model.Pairs[0].Included && !store.Current.Pairs[2].Paused, "Checkbox changed another pair or did not persist");
        var drive = Guid.NewGuid(); var root = System.IO.Path.GetPathRoot(directory)!;
        var profile = selected[2] with { SourceVolumeId = drive, SourceRelativePath = "Флешка", Source = @"Z:\Флешка" };
        var resolved = WindowsVolumes.Resolve(profile, new Dictionary<Guid, string> { [drive] = root });
        Require(resolved?.Source == System.IO.Path.Combine(root, "Флешка") && resolved.Destination == profile.Destination, "USB source did not follow volume identity");
        Require(WindowsVolumes.Resolve(profile, new Dictionary<Guid, string>()) == null, "Disconnected USB source was treated as available");
        var fromUsb = new DirectionWindow(UsbSide.Source) { Owner = owner }; fromUsb.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Require(((Panel)((ScrollViewer)fromUsb.Content).Content).Children.OfType<WrapPanel>().Single().Children.OfType<Button>().Any(b => b.Content?.ToString()?.Contains("A → B") == true), "USB-source direction is missing"); fromUsb.Close();
        var sourceA = System.IO.Path.Combine(directory, "Исходная Я" + new string('x', 100));
        var sourceB = System.IO.Path.Combine(directory, "English 🌊");
        var target = System.IO.Path.Combine(directory, "Copies");
        foreach (var path in new[] { sourceA, sourceB, target }) System.IO.Directory.CreateDirectory(path);
        var roots = FolderSelection.Map([sourceA, sourceB], target, true);
        var draft = new FolderPairDraft(new(), true, "Batch", roots, target, true, UsbSide.Source, SyncMode.Updates, false, true, false, false, 0, []);
        var created = await Task.Run(() => draft.Build([]));
        Require(created.Length == 2 && created.Select(p => p.Id).Distinct().Count() == 2 && created.All(p => p.SourceVolumeId != null && p.UsbSide == UsbSide.Source && p.ApprovedRoots == null && System.IO.Directory.Exists(p.Destination)), "Profile drafts lost roots/identity or independence");
        var conflictSource = System.IO.Path.Combine(directory, "New source"); System.IO.Directory.CreateDirectory(conflictSource);
        var blockedRoots = FolderSelection.Map([conflictSource], target, true);
        try
        {
            await Task.Run(() => (draft with { Roots = blockedRoots }).Build([new() { Source = sourceA, Destination = target }]));
            throw new Exception("Overlapping destination accepted");
        }
        catch (System.IO.IOException) { }
        Require(!System.IO.Directory.Exists(blockedRoots[0].Destination), "Preflight created an overlapping destination before rejection");
        var editor = new PairWindow(created[0], created) { Owner = owner }; editor.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); editor.Close();
        Require(editor.Results.Count == 0, "Closing the editor wrote profiles");
        await store.UpdateAsync(s => s with { Pairs = [] }); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Console.WriteLine("PASS checked multi-profile ordering, pause isolation, stop/decline, USB-source remount/disconnect and multi-folder draft preflight");
    }
}
