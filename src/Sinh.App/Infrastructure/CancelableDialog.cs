using System.Windows;

namespace Sinh.App;

internal static class CancelableDialog
{
    internal static bool? Show(Window dialog, Window owner, CancellationToken token)
    {
        dialog.Owner = owner;
        using var registration = token.Register(() => dialog.Dispatcher.InvokeAsync(() => { if (dialog.IsVisible) dialog.Close(); }));
        token.ThrowIfCancellationRequested();
        return dialog.ShowDialog();
    }
}
