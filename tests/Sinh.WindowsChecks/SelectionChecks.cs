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
        await store.UpdateAsync(s => s with { Pairs = [] }); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Console.WriteLine("PASS checked multi-profile ordering, pause isolation, stop/decline, USB-source remount and disconnect");
    }
}
