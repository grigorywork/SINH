using System.Windows;
using System.Windows.Controls;

namespace Sinh.App;

public sealed class DirectionWindow : Window
{
    public bool UseDestination { get; private set; }
    public DirectionWindow()
    {
        Title = "SINH — изменения на USB / в назначении"; Width = 620; Height = 320; MinWidth = 480; MinHeight = 280; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var p = new StackPanel { Margin = new Thickness(24) }; Content = p;
        p.Children.Add(new TextBlock { Text = "В копии обнаружены изменения", FontSize = 22, TextWrapping = TextWrapping.Wrap });
        p.Children.Add(new TextBlock { Text = "Синхронизировать папку согласно USB-носителю (B → A) или обновить копию по компьютеру (A → B)? Сначала будет показан конкретный план. Правила удаления выбранной пары сохраняются.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 16, 0, 24) });
        var buttons = new WrapPanel(); p.Children.Add(buttons);
        var usb = new Button { Content = "По USB · B → A" }; usb.Click += (_, _) => { UseDestination = true; DialogResult = true; }; buttons.Children.Add(usb);
        var pc = new Button { Content = "По компьютеру · A → B" }; pc.Click += (_, _) => { UseDestination = false; DialogResult = true; }; buttons.Children.Add(pc);
        buttons.Children.Add(new Button { Content = "Позже", IsCancel = true, Margin = new Thickness(0, 8, 0, 0) });
    }
}
