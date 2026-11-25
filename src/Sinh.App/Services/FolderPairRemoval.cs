using System.Windows;
using Sinh.Settings;

namespace Sinh.App;

internal static class FolderPairRemoval
{
    internal static async Task RemoveAsync(Window owner, SettingsStore store, FolderPair pair)
    {
        var message = Localizer.Format("RemoveProfile", pair.Name, pair.Source, pair.Destination);
        if (MessageBox.Show(owner, message, "SINH", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
        await RemoveConfirmedAsync(store, pair.Id);
    }
    internal static Task RemoveConfirmedAsync(SettingsStore store, Guid id) => store.UpdateAsync(s => ProfileList.Remove(s, [id]));
}
