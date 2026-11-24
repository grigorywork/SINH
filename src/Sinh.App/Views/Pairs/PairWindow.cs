using System.Windows;
using System.Windows.Controls;
using Sinh.Settings;
using Microsoft.Win32;

namespace Sinh.App;

public sealed class PairWindow : Window
{
    public FolderPair? Result { get; private set; }
    public PairWindow(FolderPair? original)
    {
        var pair = original ?? new();
        Title = original == null ? "SINH — новая пара" : "SINH — изменить пару";
        Width = 650; Height = 660; MinWidth = 520; MinHeight = 450; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(24) };
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        TextBox Field(string title, string value)
        {
            panel.Children.Add(new TextBlock { Text = title, Margin = new Thickness(0, 10, 0, 5) });
            var input = new TextBox { Text = value }; panel.Children.Add(input);
            System.Windows.Automation.AutomationProperties.SetName(input, title); return input;
        }
        var name = Field("Название", pair.Name);
        var source = Field("Источник A · локальная папка или UNC-путь", pair.Source);
        var pickA = new Button { Content = "Выбрать источник", Margin = new Thickness(0, 5, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        pickA.Click += (_, _) => Pick(source); panel.Children.Add(pickA);
        var target = Field("Назначение B · папка на флешке, диске или в сети", pair.Destination);
        var pickB = new Button { Content = "Выбрать назначение", Margin = new Thickness(0, 5, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        pickB.Click += (_, _) => Pick(target); panel.Children.Add(pickB);
        panel.Children.Add(new TextBlock { Text = "Режим", Margin = new Thickness(0, 14, 0, 5) });
        var mode = new ComboBox { ItemsSource = new[] { "Копирование обновлений A → B", "Зеркало A → B", "Двусторонний обмен" }, SelectedIndex = (int)pair.Mode };
        panel.Children.Add(mode);
        var deletes = new CheckBox { Content = "Разрешить перенос удалений (потребуется план)", IsChecked = pair.AllowDeletes }; panel.Children.Add(deletes);
        var history = new CheckBox { Content = "Сохранять заменённые и удалённые версии", IsChecked = pair.History }; panel.Children.Add(history);
        var connect = new CheckBox { Content = "Автоматически при подключении USB / внешнего носителя", IsChecked = pair.AutoOnConnect }; panel.Children.Add(connect);
        var watch = new CheckBox { Content = "Сверять после изменений файлов (пакетно)", IsChecked = pair.WatchChanges }; panel.Children.Add(watch);
        var interval = Field("Расписание · интервал в минутах, 0 — выключено", pair.IntervalMinutes.ToString());
        var exclusions = Field("Исключения · по одному шаблону на строку, например *.tmp", string.Join(Environment.NewLine, pair.Exclusions));
        exclusions.AcceptsReturn = true; exclusions.Height = 65;
        var note = new TextBlock { Text = "Сначала выполните один ручной запуск. Для копии выберите отдельную папку на флешке: остальные файлы носителя останутся вне пары. Для работы после извлечения флешки включите помощник в настройках автозапуска Windows.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) }; panel.Children.Add(note);
        var actions = new WrapPanel(); var save = new Button { Content = "Сохранить", IsDefault = true }; var cancel = new Button { Content = "Отмена", IsCancel = true };
        actions.Children.Add(save); actions.Children.Add(cancel); panel.Children.Add(actions);
        save.Click += async (_, _) =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(name.Text) || string.IsNullOrWhiteSpace(source.Text) || string.IsNullOrWhiteSpace(target.Text)) throw new ArgumentException("Заполните название и оба пути.");
                var a = System.IO.Path.GetFullPath(source.Text.Trim()); var b = System.IO.Path.GetFullPath(target.Text.Trim());
                if (!int.TryParse(interval.Text, out var minutes) || minutes < 0 || minutes > 10080) throw new ArgumentException("Интервал: целое число от 0 до 10080 минут.");
                save.IsEnabled = false;
                await Task.Run(() => Sinh.Core.PathRules.ValidatePair(a, b));
                var usbId = pair.UsbId; var usbRelative = pair.UsbRelativePath;
                if (connect.IsChecked == true) { var binding = await Task.Run(() => WindowsVolumes.Bind(b)); usbId = binding.Id; usbRelative = binding.Relative; }
                else if (b != pair.Destination) { usbId = null; usbRelative = null; }
                var changed = a != pair.Source || b != pair.Destination || (SyncMode)mode.SelectedIndex != pair.Mode || deletes.IsChecked != pair.AllowDeletes;
                Result = pair with { Name = name.Text.Trim(), Source = a, Destination = b, Mode = (SyncMode)mode.SelectedIndex, AllowDeletes = deletes.IsChecked == true, History = history.IsChecked == true, Exclusions = exclusions.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), ApprovedRoots = changed ? null : pair.ApprovedRoots, AutoOnConnect = connect.IsChecked == true, WatchChanges = watch.IsChecked == true, IntervalMinutes = minutes, UsbId = usbId, UsbRelativePath = usbRelative };
                DialogResult = true;
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "SINH"); }
            finally { save.IsEnabled = true; }
        };
    }
    private void Pick(TextBox field)
    {
        var dialog = new OpenFolderDialog { Title = "Выберите папку", Multiselect = false };
        if (dialog.ShowDialog(this) == true) field.Text = dialog.FolderName;
    }
}
