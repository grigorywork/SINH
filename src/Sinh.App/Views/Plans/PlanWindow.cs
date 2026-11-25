using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Sinh.Core;

namespace Sinh.App;

public sealed class PlanWindow : Window
{
    public PlanWindow(SyncPlan plan, bool canApply, string? displaySource = null, string? displayDestination = null)
    {
        Title = "SINH — план изменений"; Width = 1000; Height = 650; MinWidth = 650; MinHeight = 420; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var grid = new Grid { Margin = new Thickness(24) }; grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new()); grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); Content = grid;
        displaySource ??= plan.Request.Source; displayDestination ??= plan.Request.Destination;
        var header = new TextBlock { Text = Localizer.Format("PlanSummary", plan.Operations.Count, plan.Deletes, plan.CopyBytes / 1048576d, plan.Conflicts.Count, displaySource, displayDestination) + (plan.DestinationChanged ? "\n" + Localizer.T("В назначении обнаружены изменения после последней синхронизации.") : ""), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) }; grid.Children.Add(header);
        var table = new DataGrid { IsReadOnly = true, AutoGenerateColumns = false, ItemsSource = plan.Operations, EnableRowVirtualization = true, EnableColumnVirtualization = true, CanUserAddRows = false };
        table.Columns.Add(new DataGridTextColumn { Header = "Действие", Binding = new Binding("Action") { Converter = Localizer.CaptionConverter }, Width = 150 });
        table.Columns.Add(new DataGridTextColumn { Header = "Файл / папка", Binding = new Binding("RelativePath"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        table.Columns.Add(new DataGridTextColumn { Header = "Размер, байт", Binding = new Binding("Bytes"), Width = 130 });
        table.Columns.Add(new DataGridTextColumn { Header = "Корень назначения", Binding = new Binding("Root") { Converter = new DisplayRootConverter(plan.Source.Root, displaySource, plan.Destination.Root, displayDestination) }, Width = 200 });
        var tabs = new TabControl(); tabs.Items.Add(new TabItem { Header = "Операции", Content = table });
        if (plan.SkippedPaths.Count > 0)
        {
            var skipped = new ListBox { ItemsSource = plan.SkippedPaths };
            VirtualizingPanel.SetIsVirtualizing(skipped, true);
            tabs.Items.Add(new TabItem { Header = Localizer.T("Пропущены: исключения и ссылки") + " · " + plan.SkippedPaths.Count, Content = skipped });
        }
        if (plan.Conflicts.Count > 0)
        {
            var conflicts = new DataGrid { IsReadOnly = true, AutoGenerateColumns = false, ItemsSource = plan.Conflicts, EnableRowVirtualization = true, CanUserAddRows = false };
            conflicts.Columns.Add(new DataGridTextColumn { Header = "Файл", Binding = new Binding("RelativePath"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            conflicts.Columns.Add(new DataGridTextColumn { Header = "Решение", Binding = new Binding("Resolution") { Converter = Localizer.CaptionConverter }, Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
            tabs.Items.Add(new TabItem { Header = "Конфликты", Content = conflicts });
        }
        Grid.SetRow(tabs, 1); grid.Children.Add(tabs);
        var buttons = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) }; Grid.SetRow(buttons, 2); grid.Children.Add(buttons);
        if (canApply) { var apply = new Button { Content = "Применить этот план", IsDefault = true, IsEnabled = plan.Operations.Count > 0 }; apply.Click += (_, _) => DialogResult = true; buttons.Children.Add(apply); }
        var close = new Button { Content = canApply ? "Отмена" : "Закрыть", IsCancel = true }; buttons.Children.Add(close);
    }
    private sealed class DisplayRootConverter(string source, string displaySource, string destination, string displayDestination) : IValueConverter
    {
        public object Convert(object value, Type type, object parameter, System.Globalization.CultureInfo culture) => value as string == source ? displaySource : value as string == destination ? displayDestination : value;
        public object ConvertBack(object value, Type type, object parameter, System.Globalization.CultureInfo culture) => Binding.DoNothing;
    }
}
