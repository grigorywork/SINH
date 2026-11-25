using System.Windows;
using System.Windows.Controls;
using Sinh.Settings;

namespace Sinh.App;

public sealed partial class SettingsWindow
{
    private ComboBox PairPicker(StackPanel panel)
    {
        var picker = new ComboBox { ItemsSource = store.Current.Pairs, DisplayMemberPath = nameof(FolderPair.Name), SelectedIndex = store.Current.Pairs.Count > 0 ? 0 : -1, MinHeight = 40 };
        System.Windows.Automation.AutomationProperties.SetName(picker, Localizer.T("Пары папок"));
        picker.DropDownOpened += (_, _) =>
        {
            var id = (picker.SelectedItem as FolderPair)?.Id;
            picker.ItemsSource = store.Current.Pairs;
            picker.SelectedItem = store.Current.Pairs.FirstOrDefault(p => p.Id == id) ?? store.Current.Pairs.FirstOrDefault();
        };
        panel.Children.Add(picker); return picker;
    }
    private UIElement PairsPanel()
    {
        var panel = new StackPanel(); var picker = PairPicker(panel);
        var actions = new WrapPanel(); panel.Children.Add(actions);
        var add = new Button { Content = "Добавить папки…" }; var edit = new Button { Content = "Изменить" }; var remove = new Button { Content = "Удалить пару" };
        actions.Children.Add(add); actions.Children.Add(edit); actions.Children.Add(remove);
        async Task Change(Func<Task> action)
        {
            if (!profileChangesAllowed()) { status.Text = Localizer.T("Дождитесь завершения синхронизации."); return; }
            actions.IsEnabled = false;
            try
            {
                var selectedId = (picker.SelectedItem as FolderPair)?.Id;
                await action(); picker.ItemsSource = store.Current.Pairs;
                picker.SelectedItem = store.Current.Pairs.FirstOrDefault(p => p.Id == selectedId) ?? store.Current.Pairs.FirstOrDefault();
            }
            catch (Exception ex) { status.Text = Localizer.Error(ex); }
            finally { actions.IsEnabled = true; }
        }
        add.Click += async (_, _) => await Change(() => FolderPairEditor.EditAsync(this, store, null));
        edit.Click += async (_, _) => { if (picker.SelectedItem is FolderPair pair) await Change(() => FolderPairEditor.EditAsync(this, store, pair)); };
        remove.Click += async (_, _) =>
        {
            if (picker.SelectedItem is not FolderPair pair) return;
            if (MessageBox.Show(this, Localizer.T("Удалить пару? Файлы сохранятся."), "SINH", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
            await Change(() => store.UpdateAsync(s => s with { Pairs = s.Pairs.Where(p => p.Id != pair.Id).ToArray() }));
        };
        return panel;
    }
    private UIElement HistoryPanel()
    {
        var panel = new StackPanel(); var picker = PairPicker(panel);
        var open = new Button { Content = "История и восстановление", HorizontalAlignment = HorizontalAlignment.Left }; panel.Children.Add(open);
        open.Click += (_, _) =>
        {
            if (!profileChangesAllowed()) { status.Text = Localizer.T("Дождитесь завершения синхронизации."); return; }
            if (picker.SelectedItem is FolderPair selected && store.Current.Pairs.FirstOrDefault(p => p.Id == selected.Id) is { } pair)
                new HistoryWindow(pair, store.Current.HistoryLimitMiB * 1048576) { Owner = this }.ShowDialog();
        };
        return panel;
    }
}
