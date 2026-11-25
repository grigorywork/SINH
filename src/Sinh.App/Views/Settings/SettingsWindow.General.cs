using System.Windows;
using System.Windows.Controls;
using Sinh.Settings;

namespace Sinh.App;

public sealed partial class SettingsWindow
{
    private UIElement StartupPanel()
    {
        var panel = new StackPanel();
        var startup = new CheckBox { Content = "Автозапуск Windows", IsChecked = store.Current.AutoStart };
        var changing = false;
        startup.Click += async (_, _) =>
        {
            if (changing) return;
            if (startup.IsChecked == true && !profileChangesAllowed())
            {
                startup.IsChecked = store.Current.AutoStart;
                status.Text = Localizer.T("Дождитесь завершения синхронизации перед установкой помощника."); return;
            }
            changing = true; startup.IsEnabled = false; var old = store.Current.AutoStart;
            try
            {
                var enabled = startup.IsChecked == true;
                var relaunch = await StartupManager.SetEnabledAsync(enabled);
                await store.UpdateAsync(s => s with { AutoStart = enabled });
                status.Text = enabled ? "Автозапуск включён. Помощник установлен на компьютер." : "Автозапуск выключен.";
                if (relaunch != null) { Close(); StartupManager.RelaunchLocal(relaunch); }
            }
            catch (Exception ex)
            {
                startup.IsChecked = old; status.Text = ex.Message;
                try { await StartupManager.SetEnabledAsync(old); } catch (Exception rollback) { status.Text += " · " + rollback.Message; }
            }
            finally { changing = false; startup.IsEnabled = true; }
        };
        panel.Children.Add(startup);
        void Toggle(string label, bool initial, Action<bool> save)
        {
            var check = new CheckBox { Content = label, IsChecked = initial };
            check.Click += (_, _) => save(check.IsChecked == true); panel.Children.Add(check);
        }
        Toggle("Старт свёрнутым", store.Current.StartMinimized, v => Save(s => s with { StartMinimized = v }));
        panel.Children.Add(Text("При закрытии главного окна"));
        var group = Guid.NewGuid().ToString();
        foreach (var (label, value) in new[] { ("Завершать SINH", CloseBehavior.Exit), ("Оставлять в трее", CloseBehavior.Tray) })
        {
            var radio = new RadioButton { Content = label, GroupName = group, IsChecked = store.Current.CloseBehavior == value, Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 8) };
            radio.Checked += (_, _) => Save(s => s with { CloseBehavior = value }); panel.Children.Add(radio);
        }
        Toggle("Уведомления об успешной синхронизации", store.Current.NotifySuccess, v => Save(s => s with { NotifySuccess = v }));
        Toggle("Уведомления об ошибках", store.Current.NotifyError, v => Save(s => s with { NotifyError = v }));
        Toggle("Уведомления о конфликтах", store.Current.NotifyConflict, v => Save(s => s with { NotifyConflict = v }));
        return panel;
    }
    private UIElement PerformancePanel()
    {
        var panel = new StackPanel();
        Numeric(panel, "Лимит скорости, МиБ/с · 0 без ограничения", store.Current.SpeedLimitMiB, 0, 10240, v => Save(s => s with { SpeedLimitMiB = v }));
        Numeric(panel, "Контрольная сверка, мин", store.Current.ScanMinutes, 1, 1440, v => Save(s => s with { ScanMinutes = v }));
        Numeric(panel, "История, МиБ", (int)store.Current.HistoryLimitMiB, 16, 1048576, v => Save(s => s with { HistoryLimitMiB = v }));
        Numeric(panel, "Журнал, дней", store.Current.LogDays, 1, 3650, v => Save(s => s with { LogDays = v }));
        return panel;
    }
    private void Numeric(StackPanel panel, string label, int current, int min, int max, Action<int> save)
    {
        panel.Children.Add(Text(label));
        var row = new DockPanel(); var apply = new Button { Content = "Применить", Margin = new Thickness(8, 0, 0, 0) }; DockPanel.SetDock(apply, Dock.Right); row.Children.Add(apply);
        var input = new TextBox { Text = current.ToString() }; row.Children.Add(input); panel.Children.Add(row);
        System.Windows.Automation.AutomationProperties.SetName(input, label);
        apply.Click += (_, _) => { if (int.TryParse(input.Text, out var value) && value >= min && value <= max) save(value); else status.Text = $"Введите целое число от {min} до {max}."; };
    }
}
