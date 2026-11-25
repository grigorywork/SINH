using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Sinh.Settings;

namespace Sinh.App;

public sealed class PairWindow : Window
{
    public IReadOnlyList<FolderPair> Results { get; private set; } = [];
    public PairWindow(FolderPair? original, IReadOnlyList<FolderPair>? existing = null)
    {
        var pair = original ?? new();
        Title = original == null ? "SINH — новая пара" : "SINH — изменить пару";
        Width = 650; Height = 700; MinWidth = 520; MinHeight = 450; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(24) };
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        TextBox Field(string title, string value)
        {
            panel.Children.Add(new TextBlock { Text = title, Margin = new Thickness(0, 10, 0, 5) });
            var input = new TextBox { Text = value }; panel.Children.Add(input);
            System.Windows.Automation.AutomationProperties.SetName(input, title); return input;
        }
        var name = Field("Название", pair.Name);
        var direction = new WrapPanel(); panel.Children.Add(direction);
        var pc = new RadioButton { Content = "ПК → USB", GroupName = "Direction", IsChecked = pair.UsbSide == UsbSide.Destination, Margin = new Thickness(0, 12, 20, 8), MinHeight = 40 };
        var usb = new RadioButton { Content = "USB → ПК", GroupName = "Direction", IsChecked = pair.UsbSide == UsbSide.Source, MinHeight = 40 };
        direction.Children.Add(pc); direction.Children.Add(usb);
        var source = Field("Источник", pair.Source);
        var selection = new StackPanel(); panel.Children.Add(selection);
        var choices = new List<CheckBox>(); var grouped = false;
        var pickA = new Button { Content = original == null ? "Выбрать папки…" : "Выбрать папку…", HorizontalAlignment = HorizontalAlignment.Left };
        pickA.Click += (_, _) =>
        {
            var picker = new OpenFolderDialog { Title = Localizer.T("Выберите папки"), Multiselect = original == null };
            if (picker.ShowDialog(this) != true) return;
            choices.Clear(); selection.Children.Clear(); grouped = picker.FolderNames.Length > 1;
            source.IsReadOnly = grouped;
            if (!grouped) { source.Text = picker.FolderName; return; }
            source.Text = "";
            foreach (var path in picker.FolderNames)
            {
                var check = new CheckBox { Content = path, Tag = path, IsChecked = true, MinHeight = 40 };
                choices.Add(check); selection.Children.Add(check);
            }
        }; panel.Children.Add(pickA);
        var target = Field("Назначение", pair.Destination);
        var pickB = new Button { Content = "Выбрать назначение…", HorizontalAlignment = HorizontalAlignment.Left };
        pickB.Click += (_, _) => { var picker = new OpenFolderDialog(); if (picker.ShowDialog(this) == true) target.Text = picker.FolderName; }; panel.Children.Add(pickB);
        var hint = new TextBlock { Text = "Несколько папок → отдельные копии внутри назначения.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 4) }; panel.Children.Add(hint);
        panel.Children.Add(new TextBlock { Text = "Режим", Margin = new Thickness(0, 14, 0, 5) });
        var mode = new ComboBox { ItemsSource = new[] { "Копирование обновлений A → B", "Зеркало A → B", "Двусторонний обмен" }.Select(Localizer.T).ToArray(), SelectedIndex = (int)pair.Mode }; panel.Children.Add(mode);
        var deletes = new CheckBox { Content = "Переносить удаления", IsChecked = pair.AllowDeletes }; panel.Children.Add(deletes);
        var history = new CheckBox { Content = "История файлов", IsChecked = pair.History }; panel.Children.Add(history);
        var connect = new CheckBox { Content = "При подключении USB", IsChecked = pair.AutoOnConnect }; panel.Children.Add(connect);
        var watch = new CheckBox { Content = "При изменении файлов", IsChecked = pair.WatchChanges }; panel.Children.Add(watch);
        var interval = Field("Интервал, мин · 0 — выключено", pair.IntervalMinutes.ToString());
        var exclusions = Field("Исключения · по одному шаблону на строку", string.Join(Environment.NewLine, pair.Exclusions)); exclusions.AcceptsReturn = true; exclusions.Height = 65;
        panel.Children.Add(new TextBlock { Text = "Первый запуск — вручную. Для работы без открытого окна включите автозапуск SINH.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) });
        var actions = new WrapPanel(); var save = new Button { Content = "Сохранить", IsDefault = true }; var cancel = new Button { Content = "Отмена", IsCancel = true };
        actions.Children.Add(save); actions.Children.Add(cancel); panel.Children.Add(actions);
        var saving = false;
        Closing += (_, e) => { if (saving) e.Cancel = true; };
        save.Click += async (_, _) =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(name.Text) || string.IsNullOrWhiteSpace(target.Text)) throw new ArgumentException("Заполните название и оба пути.");
                var roots = FolderSelection.Map(grouped ? choices.Where(c => c.IsChecked == true).Select(c => (string)c.Tag) : [source.Text], target.Text, grouped);
                if (!int.TryParse(interval.Text, out var minutes) || minutes < 0 || minutes > 10080) throw new ArgumentException("Интервал: целое число от 0 до 10080 минут.");
                var side = usb.IsChecked == true ? UsbSide.Source : UsbSide.Destination;
                var automatic = connect.IsChecked == true;
                var selectedMode = (SyncMode)mode.SelectedIndex; var allowDeletes = deletes.IsChecked == true;
                var keepHistory = history.IsChecked == true; var watchChanges = watch.IsChecked == true;
                var title = name.Text.Trim(); var patterns = exclusions.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var parent = Path.GetFullPath(target.Text);
                var draft = new FolderPairDraft(pair, original == null, title, roots, parent, grouped, side, selectedMode, allowDeletes, keepHistory, automatic, watchChanges, minutes, patterns);
                saving = true; IsEnabled = false;
                var results = await Task.Run(() => draft.Build(existing ?? []));
                saving = false;
                Results = results; DialogResult = true;
            }
            catch (Exception ex) { MessageBox.Show(this, Localizer.Error(ex), "SINH"); }
            finally { saving = false; IsEnabled = true; }
        };
    }
}
