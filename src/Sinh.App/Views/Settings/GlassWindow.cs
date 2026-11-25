using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Sinh.Settings;

namespace Sinh.App;

public sealed class GlassWindow : Window
{
    private sealed record Choice(string Key, string Label);
    public Appearance? Result { get; private set; }
    public GlassWindow(Appearance original)
    {
        Title = "SINH — редактор стекла"; Width = 540; Height = 630; MinWidth = 430; MinHeight = 500; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var p = new StackPanel { Margin = new Thickness(24) }; Content = new ScrollViewer { Content = p };
        var preview = new Border { Height = 100, CornerRadius = new CornerRadius(14), Margin = new Thickness(0, 0, 0, 12), Child = new TextBlock { Text = "SINH · Пример текста", Foreground = Brushes.Black, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, FontSize = 20 } }; p.Children.Add(preview);
        p.Children.Add(new TextBlock { Text = "Windows 11: системный Mica при доступном GPU. Windows 10, слабый GPU или недоступный эффект: лёгкая градиентная поверхность. Текст не размывается.", TextWrapping = TextWrapping.Wrap });
        Slider SliderField(string label, double value)
        { p.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 14, 0, 5) }); var s = new Slider { Minimum = 0, Maximum = 100, Value = value, MinHeight = 40, TickFrequency = 1, IsSnapToTickEnabled = true }; System.Windows.Automation.AutomationProperties.SetName(s, label); p.Children.Add(s); return s; }
        var opacity = SliderField("Прозрачность, %", original.Transparency);
        var frost = SliderField("Матовость поверхности, %", original.Frost);
        ComboBox Select(string label, (string Key, string Label)[] values, string value)
        { p.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 10, 0, 5) }); var combo = new ComboBox { ItemsSource = values.Select(v => new Choice(v.Key, Localizer.T(v.Label))).ToArray(), DisplayMemberPath = "Label", SelectedValuePath = "Key", SelectedValue = value }; System.Windows.Automation.AutomationProperties.SetName(combo, label); p.Children.Add(combo); return combo; }
        var tint = Select("Оттенок", [("Theme", "По теме"), ("Blue", "Голубой"), ("Teal", "Бирюзовый"), ("Lilac", "Сиреневый"), ("Pink", "Розовый"), ("Amber", "Янтарный"), ("Green", "Зелёный"), ("Graphite", "Графитовый")], original.Tint);
        var background = Select("Рисунок", [("Aurora", "Аврора"), ("Stripes", "Полосы"), ("Solid", "Однотонный")], original.Background);
        void UpdatePreview()
        {
            var color = (tint.SelectedValue as string) switch { "Blue" => Colors.LightBlue, "Teal" => Colors.PaleTurquoise, "Pink" => Colors.LightPink, "Amber" => Colors.Wheat, "Green" => Colors.LightGreen, "Graphite" => Colors.LightSlateGray, _ => Colors.Lavender };
            var density = Math.Clamp((100 - opacity.Value) / 100 + frost.Value / 500, 0, 1);
            color = Color.FromRgb((byte)(255 - (255 - color.R) * density), (byte)(255 - (255 - color.G) * density), (byte)(255 - (255 - color.B) * density));
            preview.Background = AppearanceService.CreatePattern(background.SelectedValue as string ?? "Aurora", Colors.White, color);
        }
        opacity.ValueChanged += (_, _) => UpdatePreview(); frost.ValueChanged += (_, _) => UpdatePreview(); tint.SelectionChanged += (_, _) => UpdatePreview(); background.SelectionChanged += (_, _) => UpdatePreview(); UpdatePreview();
        var buttons = new WrapPanel { Margin = new Thickness(0, 20, 0, 0) }; p.Children.Add(buttons);
        var save = new Button { Content = "Сохранить", IsDefault = true }; save.Click += (_, _) => { Result = original with { Transparency = opacity.Value, Frost = frost.Value, Tint = tint.SelectedValue as string ?? "Theme", Background = background.SelectedValue as string ?? "Aurora" }; DialogResult = true; };
        var cancel = new Button { Content = "Отмена", IsCancel = true };
        var reset = new Button { Content = "Сбросить" }; reset.Click += (_, _) => { opacity.Value = 50; frost.Value = 0; tint.SelectedValue = "Theme"; background.SelectedValue = "Aurora"; };
        buttons.Children.Add(save); buttons.Children.Add(cancel); buttons.Children.Add(reset);
    }
}
