using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Sinh.Core;
using Sinh.Settings;

namespace Sinh.App;

public sealed partial class SettingsWindow
{
    private UIElement ConfigurationPanel()
    {
        var panel = new StackPanel();
        panel.Children.Add(Text("После импорта пары на паузе."));
        var frequencies = new[] { ("Вручную", 0), ("Ежедневно", 1), ("Раз в 3 дня", 3), ("Раз в 10 дней", 10), ("Раз в месяц", 30) };
        var group = Guid.NewGuid().ToString();
        foreach (var (label, days) in frequencies)
        {
            var choice = new RadioButton { Content = label, GroupName = group, IsChecked = store.Current.ConfigurationBackupDays == days, MinHeight = 40 };
            choice.Checked += (_, _) => Save(s => s with { ConfigurationBackupDays = days }); panel.Children.Add(choice);
        }
        var actions = new WrapPanel(); panel.Children.Add(actions);
        async Task Execute(Button button, Func<Task> action)
        {
            button.IsEnabled = false;
            try { await action(); }
            catch (Exception ex) { status.Text = Localizer.Error(ex); }
            finally { button.IsEnabled = true; }
        }
        var backup = new Button { Content = "Создать копию настроек" }; actions.Children.Add(backup);
        backup.Click += async (_, _) => await Execute(backup, async () => { await ConfigurationBackups.SaveAsync(store.Current); status.Text = ConfigurationBackups.DirectoryPath; });
        var export = new Button { Content = "Экспорт…" }; actions.Children.Add(export);
        export.Click += async (_, _) => await Execute(export, async () =>
        {
            var dialog = new SaveFileDialog { FileName = "SINH-settings.json", Filter = "JSON|*.json", DefaultExt = ".json" };
            if (dialog.ShowDialog(this) != true) return;
            await ConfigurationTransfer.ExportAsync(store.Current, dialog.FileName); status.Text = Localizer.T("Сохранено");
        });
        var import = new Button { Content = "Импорт…" }; actions.Children.Add(import);
        import.Click += async (_, _) => await Execute(import, async () =>
        {
            if (!profileChangesAllowed()) { status.Text = Localizer.T("Дождитесь завершения синхронизации перед импортом."); return; }
            var dialog = new OpenFileDialog { Filter = "JSON|*.json" }; if (dialog.ShowDialog(this) != true) return;
            var snapshot = await ConfigurationTransfer.ReadImportAsync(dialog.FileName);
            var invalid = await Task.Run(() => snapshot.Pairs.Count(pair =>
            {
                try { PathRules.ValidatePair(pair.Source, pair.Destination); return false; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return true; }
            }));
            var message = Localizer.Format("ImportSummary", snapshot.Pairs.Count, invalid);
            if (MessageBox.Show(this, message, "SINH", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
            await ConfigurationBackups.SaveAsync(store.Current);
            var previous = store.Current.AutoStart;
            await StartupManager.SetEnabledAsync(false);
            try { await store.UpdateAsync(_ => snapshot); }
            catch { await StartupManager.SetEnabledAsync(previous); throw; }
            status.Text = Localizer.T("Импортировано. Проверьте пути каждой пары перед запуском."); Close();
        });
        return panel;
    }
}
