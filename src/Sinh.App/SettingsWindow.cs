using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Sinh.Settings;

namespace Sinh.App;

public sealed class SettingsWindow : Window
{
    private readonly SettingsStore store;
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    public SettingsWindow(SettingsStore store)
    {
        this.store = store; Title = "SINH — настройки"; Width = 720; Height = 760; MinWidth = 520; MinHeight = 420; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var stack = new StackPanel { Margin = new Thickness(24) };
        Content = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        stack.Children.Add(new TextBlock { Text = "Настройки", FontSize = 26, Margin = new Thickness(0, 0, 0, 16) });
        void Section(string title, Func<UIElement> build)
        {
            var expander = new Expander { Header = title };
            expander.Expanded += (_, _) => { if (expander.Content == null) expander.Content = build(); };
            stack.Children.Add(expander);
        }
        Section("Внешний вид", AppearancePanel);
        Section("Пары папок", () => Text("Пары настраиваются на главном экране. Изменение одной пары не меняет остальные."));
        Section("Правила синхронизации", () => Text("Новые пары: ручное копирование обновлений A → B, без переноса удалений. Перед зеркалом и сменой корней потребуется план."));
        Section("Расписание", () => Text("Этап 5: расписание и запуск при подключении USB. Сейчас автоматический запуск отсутствует."));
        Section("Исключения", () => Text("У каждой пары свой список шаблонов. Шаблоны применяются к относительным путям. Ссылки и reparse points не обходятся."));
        Section("История и восстановление", HistoryPanel);
        Section("Производительность", () => Text("Один файловый поток. Сканирование и хеширование выполняются вне UI-потока. Параллельная запись в первом ядре не используется."));
        Section("Уведомления и трей", () => Text("Этап 5. Автозапуск Windows выключен. Сейчас закрытие окна завершает приложение."));
        Section("О программе", () =>
        {
            var about = new StackPanel();
            about.Children.Add(Text($"SINH {App.DisplayVersion} · Windows 10/11 x64 (целевые ОС).\nОтдельная программа; протокол NoteLS не используется.\nОформление: NoteLS 1.10.58, b1f4b1f8…\nСборка не подписана. Windows-запуск и DPI-проверки ещё не выполнены."));
            about.Children.Add(new DeveloperSignature { HorizontalAlignment = HorizontalAlignment.Left });
            return about;
        });
        stack.Children.Add(status);
        var close = new Button { Content = "Закрыть", IsCancel = true, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0) }; stack.Children.Add(close);
    }
    private static TextBlock Text(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 8) };
    private async void Save(Func<AppSettings, AppSettings> update)
    {
        try { await store.UpdateAsync(update); status.Text = "Сохранено"; }
        catch (Exception ex) { status.Text = "Не удалось сохранить: " + ex.Message; }
    }
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
        Choices("Тема", new[] { ("Системная", Theme.System), ("Светлая", Theme.Light), ("Тёмная", Theme.Dark) }, a.Theme, v => Save(s => s with { Appearance = s.Appearance with { Theme = v } }));
        Choices("Стиль", new[] { ("Классика", false), ("Матовое стекло", true) }, a.Glass, v => Save(s => s with { Appearance = s.Appearance with { Glass = v } }));
        Choices("Размер шрифта", new[] { ("Компактный", .85), ("Обычный", 1d), ("Крупный", 1.15) }, a.Scale, v => Save(s => s with { Appearance = s.Appearance with { Scale = v } }));
        Choices("Акцент", new[] { ("Системный", "System"), ("Фиолетовый", "Purple"), ("Синий", "Blue"), ("Зелёный", "Green"), ("Оранжевый", "Orange"), ("Розовый", "Pink"), ("Бирюзовый", "Teal") }, a.Accent, v => Save(s => s with { Appearance = s.Appearance with { Accent = v } }));
        panel.Children.Add(Text("Шрифт · системные семейства"));
        var font = new ComboBox { ItemsSource = Fonts.SystemFontFamilies.Select(f => f.Source).Order().ToArray(), SelectedItem = a.Font };
        font.SelectionChanged += (_, _) => { if (font.SelectedItem is string f) Save(s => s with { Appearance = s.Appearance with { Font = f } }); }; panel.Children.Add(font);
        panel.Children.Add(Text("Встроенные семейства NoteLS и английская локализация пока не включены в этот этап."));
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
        foreach (var (kind, label) in new[] { (IconStyle.Glass, "Розовое стекло (1Б)"), (IconStyle.Frosted, "Матовый (3)"), (IconStyle.Paper, "Лист с галочкой") })
        {
            var content = new StackPanel(); content.Children.Add(new Image { Source = new BitmapImage(new Uri($"pack://application:,,,/Assets/{kind.ToString().ToLowerInvariant()}.png")), Width = 64, Height = 64 }); content.Children.Add(Text(label));
            var radio = new RadioButton { Content = content, GroupName = "Icons", IsChecked = a.Icon == kind, Margin = new Thickness(0, 0, 12, 8), Padding = new Thickness(8) };
            radio.Checked += (_, _) => Save(s => s with { Appearance = s.Appearance with { Icon = kind } }); icons.Children.Add(radio);
        }
        panel.Children.Add(icons); panel.Children.Add(Text("Значок окна меняется сразу. Кеш закреплённых значков Windows обновляет самостоятельно."));
        return panel;
    }
    private UIElement HistoryPanel()
    {
        var p = new StackPanel(); p.Children.Add(Text($"Заменённые и удалённые версии сохраняются на этом компьютере отдельно от синхронизируемых папок. Лимит: {store.Current.HistoryLimitMiB} МиБ. При заполнении операция остановится. Автоматическая очистка выключена. Просмотр и восстановление: выберите пару на главной и нажмите «История».")); return p;
    }
}

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
            color = Color.FromRgb((byte)(255 - (255-color.R)*(100-opacity.Value)/100), (byte)(255 - (255-color.G)*(100-opacity.Value)/100), (byte)(255 - (255-color.B)*(100-opacity.Value)/100));
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
