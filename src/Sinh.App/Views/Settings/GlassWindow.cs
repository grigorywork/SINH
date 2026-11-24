using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Sinh.Settings;

namespace Sinh.App;

public sealed class GlassWindow : Window
{
    public Appearance? Result { get; private set; }
    public GlassWindow(Appearance original)
    {
        Title = "SINH — редактор стекла"; Width = 540; Height = 630; MinWidth = 430; MinHeight = 500; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var p = new StackPanel { Margin = new Thickness(24) }; Content = new ScrollViewer { Content = p };
        var preview = new Border { Height = 100, CornerRadius = new CornerRadius(14), Margin = new Thickness(0, 0, 0, 12), Child = new TextBlock { Text = "SINH · Пример текста", Foreground = Brushes.Black, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, FontSize = 20 } }; p.Children.Add(preview);
        p.Children.Add(new TextBlock { Text = "Лёгкая градиентная поверхность. Размытие фона и системный Acrylic ещё не реализованы.", TextWrapping = TextWrapping.Wrap });
        Slider SliderField(string label, double value)
        { p.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 14, 0, 5) }); var s = new Slider { Minimum = 0, Maximum = 100, Value = value, MinHeight = 38, TickFrequency = 1, IsSnapToTickEnabled = true }; p.Children.Add(s); return s; }
        var opacity = SliderField("Прозрачность, %", original.Transparency);
        var frost = SliderField("Матовость, % · параметр сохранится; эффект появится позже", original.Frost);
        ComboBox Select(string label, string[] values, string value)
        { p.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 10, 0, 5) }); var combo = new ComboBox { ItemsSource = values, SelectedItem = value }; p.Children.Add(combo); return combo; }
        var tint = Select("Оттенок", ["Theme", "Blue", "Teal", "Lilac", "Pink", "Amber", "Green", "Graphite"], original.Tint);
        var background = Select("Рисунок", ["Aurora", "Stripes", "Solid"], original.Background);
        void UpdatePreview()
        {
            var color = (tint.SelectedItem as string) switch { "Blue" => Colors.LightBlue, "Teal" => Colors.PaleTurquoise, "Pink" => Colors.LightPink, "Amber" => Colors.Wheat, "Green" => Colors.LightGreen, "Graphite" => Colors.LightSlateGray, _ => Colors.Lavender };
            color = Color.FromRgb((byte)(255 - (255 - color.R) * (100 - opacity.Value) / 100), (byte)(255 - (255 - color.G) * (100 - opacity.Value) / 100), (byte)(255 - (255 - color.B) * (100 - opacity.Value) / 100));
            preview.Background = background.SelectedItem as string == "Solid" ? new SolidColorBrush(color) : new LinearGradientBrush(color, Colors.White, 35);
        }
        opacity.ValueChanged += (_, _) => UpdatePreview(); tint.SelectionChanged += (_, _) => UpdatePreview(); background.SelectionChanged += (_, _) => UpdatePreview(); UpdatePreview();
        var buttons = new WrapPanel { Margin = new Thickness(0, 20, 0, 0) }; p.Children.Add(buttons);
        var save = new Button { Content = "Сохранить", IsDefault = true }; save.Click += (_, _) => { Result = original with { Transparency = opacity.Value, Frost = frost.Value, Tint = tint.SelectedItem as string ?? "Theme", Background = background.SelectedItem as string ?? "Aurora" }; DialogResult = true; };
        var cancel = new Button { Content = "Отмена", IsCancel = true };
        var reset = new Button { Content = "Сбросить" }; reset.Click += (_, _) => { opacity.Value = 50; frost.Value = 0; tint.SelectedItem = "Theme"; background.SelectedItem = "Aurora"; };
        buttons.Children.Add(save); buttons.Children.Add(cancel); buttons.Children.Add(reset);
    }
}
