using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Sinh.Core;
using Sinh.Settings;

namespace Sinh.App;

public sealed class HistoryWindow : Window
{
    private readonly DataGrid table = new() { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, EnableRowVirtualization = true };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly HistoryStore history = new(App.DataDirectory);
    private readonly FolderPair pair;
    private readonly long limit;
    private readonly CancellationTokenSource cancellation = new();
    public HistoryWindow(FolderPair pair, long limit)
    {
        this.pair = pair; this.limit = limit; Title = "SINH — история файлов"; Width = 960; Height = 600; MinWidth = 600; MinHeight = 400; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var grid = new Grid { Margin = new Thickness(24) }; grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new()); grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); Content = grid;
        status.Text = $"История пары «{pair.Name}» · лимит всей истории {limit / 1048576} МиБ. Хранится на этом компьютере. Автоматическая очистка выключена."; grid.Children.Add(status);
        table.Columns.Add(new DataGridTextColumn { Header = "Дата", Binding = new Binding("CreatedAt") { StringFormat = "g" }, Width = 160 }); table.Columns.Add(new DataGridTextColumn { Header = "Исходный путь", Binding = new Binding("OriginalPath"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) }); table.Columns.Add(new DataGridTextColumn { Header = "Байт", Binding = new Binding("Version.Length"), Width = 100 }); Grid.SetRow(table, 1); grid.Children.Add(table);
        var buttons = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) }; Grid.SetRow(buttons, 2); grid.Children.Add(buttons);
        var restore = new Button { Content = "Восстановить выбранную версию" }; buttons.Children.Add(restore);
        var remove = new Button { Content = "Удалить версию из истории" }; buttons.Children.Add(remove);
        buttons.Children.Add(new Button { Content = "Закрыть", IsCancel = true });
        remove.Click += async (_, _) =>
        {
            if (table.SelectedItem is not HistoryEntry selected || !restore.IsEnabled) return;
            if (MessageBox.Show(this, Localizer.T("Безвозвратно удалить выбранную архивную версию?") + "\n" + selected.OriginalPath, "SINH", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
            restore.IsEnabled = remove.IsEnabled = false;
            try { await history.DeleteAsync(selected, cancellation.Token); await LoadAsync(); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { status.Text = Localizer.Error(ex); }
            finally { restore.IsEnabled = remove.IsEnabled = true; }
        };
        restore.Click += async (_, _) =>
        {
            if (table.SelectedItem is not HistoryEntry selected) return;
            restore.IsEnabled = remove.IsEnabled = false;
            try
            {
                var target = await Task.Run(() => HistoryTarget.Resolve(selected, pair));
                if (MessageBox.Show(this, Localizer.T("Восстановить файл?") + "\n" + target.Entry.OriginalPath + "\n" + Localizer.T("Текущая версия, если есть, попадёт в историю."), "SINH", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
                await Task.Run(() => history.RestoreAsync(target.Entry, [target.Root], limit, cancellation.Token, target.Available));
                status.Text = Localizer.T("Версия восстановлена. Перед следующей синхронизацией сверка обнаружит это изменение."); await LoadAsync();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { status.Text = ex.Message; }
            finally { restore.IsEnabled = remove.IsEnabled = true; }
        };
        Loaded += async (_, _) => await LoadAsync(); Closed += (_, _) => cancellation.Cancel();
    }
    private async Task LoadAsync()
    { try { table.ItemsSource = await Task.Run(() => history.ListAsync(pair.Id, cancellation.Token)); if(history.Warnings.Count>0)status.Text=Localizer.T("Некоторые записи истории повреждены. Оригиналы сохранены:") + "\n" + string.Join("\n",history.Warnings.Take(3)); } catch (OperationCanceledException) { } catch (Exception ex) { status.Text = Localizer.Error(ex); } }
}
