using System.IO;
using System.Windows;
using Sinh.Settings;
using Forms = System.Windows.Forms;

namespace Sinh.App;

internal sealed class TrayService : IDisposable
{
    private readonly Forms.NotifyIcon tray = new() { Text = "SINH · MSS9LN", Visible = false };
    private readonly SettingsStore store;
    private IconStyle? currentIcon;
    internal TrayService(SettingsStore store)
    {
        this.store = store;
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Открыть SINH", null, (_, _) => ((App)System.Windows.Application.Current).ShowMain());
        menu.Items.Add("Пауза всех пар", null, async (_, _) =>
        {
            try { await store.UpdateAsync(s => s with { Pairs = s.Pairs.Select(p => p with { Paused = true }).ToArray() }); }
            catch (Exception ex) { Notify("Ошибка", ex.Message, "error"); }
        });
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => ((App)System.Windows.Application.Current).RequestExit());
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => ((App)System.Windows.Application.Current).ShowMain();
        store.Changed += SettingsChanged; ApplyIcon(); tray.Visible = true;
    }
    private void SettingsChanged(object? sender, AppSettings e) => System.Windows.Application.Current.Dispatcher.InvokeAsync(ApplyIcon);
    private void ApplyIcon()
    {
        var menu=tray.ContextMenuStrip;
        if(menu!=null){menu.Items[0].Text=Localizer.T("Открыть SINH");menu.Items[1].Text=Localizer.T("Пауза всех пар");menu.Items[3].Text=Localizer.T("Выход");}
        var icon = store.Current.Appearance.Icon;
        if (currentIcon == icon) return;
        var resource = System.Windows.Application.GetResourceStream(new Uri($"pack://application:,,,/Assets/{icon.ToString().ToLowerInvariant()}.ico"));
        using var stream = resource!.Stream;
        var previous = tray.Icon; tray.Icon = new System.Drawing.Icon(stream); previous?.Dispose(); currentIcon = icon;
    }
    internal void Notify(string title, string message, string kind)
    {
        var s = store.Current;
        if (kind == "success" && !s.NotifySuccess || kind == "error" && !s.NotifyError || kind == "conflict" && !s.NotifyConflict) return;
        tray.ShowBalloonTip(5000, "SINH · " + Localizer.T(title), message.Length > 240 ? message[..240] : message, kind == "error" ? Forms.ToolTipIcon.Error : kind == "conflict" ? Forms.ToolTipIcon.Warning : Forms.ToolTipIcon.Info);
    }
    public void Dispose() { store.Changed -= SettingsChanged; tray.Visible = false; var icon = tray.Icon; var menu = tray.ContextMenuStrip; tray.Dispose(); icon?.Dispose(); menu?.Dispose(); }
}
