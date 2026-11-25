using System.Windows;
using Sinh.Settings;
using System.Windows.Controls;

namespace Sinh.App;

public sealed class DirectionWindow : Window
{
    public bool UseDestination { get; private set; }
    public DirectionWindow(UsbSide side = UsbSide.Destination)
    {
        Title = "SINH — выбрать направление"; Width = 620; Height = 320; MinWidth = 480; MinHeight = 280; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var p = new StackPanel { Margin = new Thickness(24) }; Content = new ScrollViewer { Content = p, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var heading = new TextBlock { Text = "В копии обнаружены изменения", TextWrapping = TextWrapping.Wrap }; heading.SetResourceReference(TextBlock.FontSizeProperty, "SectionFontSize"); p.Children.Add(heading);
        p.Children.Add(new TextBlock { Text = "Выберите источник изменений. Далее — план синхронизации.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 16, 0, 24) });
        var buttons = new WrapPanel(); p.Children.Add(buttons);
        var usb = new Button { Content = side == UsbSide.Destination ? "По USB · B → A" : "По компьютеру · B → A" }; usb.Click += (_, _) => { UseDestination = true; DialogResult = true; }; buttons.Children.Add(usb);
        var pc = new Button { Content = side == UsbSide.Destination ? "По компьютеру · A → B" : "По USB · A → B" }; pc.Click += (_, _) => { UseDestination = false; DialogResult = true; }; buttons.Children.Add(pc);
        buttons.Children.Add(new Button { Content = "Позже", IsCancel = true, Margin = new Thickness(0, 8, 0, 0) });
    }
}
