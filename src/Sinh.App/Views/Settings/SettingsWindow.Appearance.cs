using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Sinh.Settings;

namespace Sinh.App;

public sealed partial class SettingsWindow
{
    private StackPanel AppearancePanel()
    {
        var panel = new StackPanel(); var a = store.Current.Appearance;
        void Choices<T>(string label, (string Text, T Value)[] values, T selected, Action<T> change)
        {
            panel.Children.Add(Text(label)); var pills = new WrapPanel(); var group = Guid.NewGuid().ToString();
            foreach (var option in values)
            {
                var radio = new RadioButton { Content = option.Text, GroupName = group, IsChecked = EqualityComparer<T>.Default.Equals(selected, option.Value), MinHeight = 38, Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 8, 8), VerticalContentAlignment = VerticalAlignment.Center };
                radio.Checked += (_, _) => change(option.Value); pills.Children.Add(radio);
            }
            panel.Children.Add(pills);
        }
        Choices("Язык", new[] { ("Русский", "ru"), ("English", "en") }, a.Language, v => Save(s => s with { Appearance = s.Appearance with { Language = v } }));
        Choices("Тема", new[] { ("Системная", Theme.System), ("Светлая", Theme.Light), ("Тёмная", Theme.Dark) }, a.Theme, v => Save(s => s with { Appearance = s.Appearance with { Theme = v } }));
        Choices("Стиль", new[] { ("Классика", false), ("Матовое стекло", true) }, a.Glass, v => Save(s => s with { Appearance = s.Appearance with { Glass = v } }));
        Choices("Размер шрифта", new[] { ("Компактный", .85), ("Обычный", 1d), ("Крупный", 1.15) }, a.Scale, v => Save(s => s with { Appearance = s.Appearance with { Scale = v } }));
        Choices("Акцент", new[] { ("Системный", "System"), ("Фиолетовый", "Purple"), ("Синий", "Blue"), ("Зелёный", "Green"), ("Оранжевый", "Orange"), ("Розовый", "Pink"), ("Бирюзовый", "Teal") }, a.Accent, v => Save(s => s with { Appearance = s.Appearance with { Accent = v } }));
        panel.Children.Add(Text("Шрифт · встроенные и системные семейства"));
        var fontLabel = new FrameworkElementFactory(typeof(TextBlock)); fontLabel.SetBinding(TextBlock.TextProperty, Localizer.CaptionBinding("Label"));
        var font = new ComboBox { ItemsSource = FontCatalog.Choices().ToArray(), ItemTemplate = new DataTemplate { VisualTree = fontLabel }, SelectedValuePath = "Key", SelectedValue = a.Font, IsTextSearchEnabled = true };
        TextSearch.SetTextPath(font, "Label");
        font.SelectionChanged += (_, _) => { if (font.SelectedValue is string f) Save(s => s with { Appearance = s.Appearance with { Font = f } }); }; panel.Children.Add(font);
        panel.Children.Add(Text("Шрифты доступны без интернета; загружается выбранное семейство. Лицензии находятся в пакете приложения."));
        var glass = new Button { Content = "Редактор стекла…", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
        glass.Click += async (_, _) =>
        {
            var editor = new GlassWindow(store.Current.Appearance) { Owner = this };
            if (editor.ShowDialog() == true && editor.Result is { } result)
            {
                try { await store.UpdateAsync(s => s with { Appearance = s.Appearance with { Transparency = result.Transparency, Frost = result.Frost, Tint = result.Tint, Background = result.Background } }); status.Text = "Сохранено"; }
                catch (Exception ex) { status.Text = ex.Message; }
            }
        }; panel.Children.Add(glass);
        panel.Children.Add(Text("Значок · независимо от темы"));
        var icons = new WrapPanel();
        foreach (var (kind, label) in new[] { (IconStyle.Mirror, "Зеркальность SINH"), (IconStyle.Glass, "Розовое стекло (1Б)"), (IconStyle.Frosted, "Матовый (3)"), (IconStyle.Paper, "Лист с галочкой") })
        {
            var content = new StackPanel(); content.Children.Add(new Image { Source = new BitmapImage(AssetUris.For($"Assets/{kind.ToString().ToLowerInvariant()}.png")), Width = 64, Height = 64 }); content.Children.Add(Text(label));
            var radio = new RadioButton { Content = content, GroupName = "Icons", IsChecked = a.Icon == kind, Margin = new Thickness(0, 0, 12, 8), Padding = new Thickness(8) };
            radio.Checked += (_, _) => Save(s => s with { Appearance = s.Appearance with { Icon = kind } }); icons.Children.Add(radio);
        }
        panel.Children.Add(icons); panel.Children.Add(Text("Значок окна меняется сразу. Кеш закреплённых значков Windows обновляет самостоятельно."));
        return panel;
    }
}
