using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Sinh.Settings;

namespace Sinh.App;

public sealed partial class SettingsWindow : Window
{
    private readonly SettingsStore store;
    private readonly Func<bool> profileChangesAllowed;
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    public SettingsWindow(SettingsStore store, Func<bool>? profileChangesAllowed = null)
    {
        this.profileChangesAllowed = profileChangesAllowed ?? (() => true);
        this.store = store; Title = "SINH — настройки"; Width = 720; Height = 760; MinWidth = 520; MinHeight = 420; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var stack = new StackPanel { Margin = new Thickness(24) };
        Content = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var heading = new TextBlock { Text = "Настройки", Margin = new Thickness(0, 0, 0, 16) }; heading.SetResourceReference(TextBlock.FontSizeProperty, "DialogTitleFontSize"); stack.Children.Add(heading);
        void Section(string title, Func<UIElement> build)
        {
            var expander = new Expander { Header = title };
            expander.Expanded += (_, _) => { if (expander.Content == null) expander.Content = build(); if (expander.Content is DependencyObject content) Localizer.Attach(content); if (expander.Content is UIElement element) WindowMotion.Reveal(element); };
            stack.Children.Add(expander);
        }
        Section("Внешний вид", AppearancePanel);
        Section("Пары папок", PairsPanel);
        Section("История и восстановление", HistoryPanel);
        Section("Дополнительно", () =>
        {
            var panel = new StackPanel();
            panel.Children.Add(StartupPanel()); panel.Children.Add(PerformancePanel());
            panel.Children.Add(Text("Копия настроек")); panel.Children.Add(ConfigurationPanel());
            return panel;
        });
        Section("О программе", () =>
        {
            var about = new StackPanel();
            var description = Text(""); description.SetBinding(TextBlock.TextProperty, Localizer.FormattedTranslation("About", App.DisplayVersion)); about.Children.Add(description);
            about.Children.Add(new DeveloperSignature { HorizontalAlignment = HorizontalAlignment.Left });
            return about;
        });
        stack.Children.Add(status);
        var close = new Button { Content = "Закрыть", IsCancel = true, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0) }; stack.Children.Add(close);
        Closing += async (_, e) =>
        {
            if (!store.HasPendingSaves) return;
            e.Cancel = true; IsEnabled = false;
            try { await store.FlushAsync(); IsEnabled = true; Close(); }
            catch (Exception ex) { IsEnabled = true; status.Text = ex.Message; }
        };
    }
    private static TextBlock Text(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 8) };
    private async void Save(Func<AppSettings, AppSettings> update)
    {
        try { await store.UpdateAsync(update); status.Text = "Сохранено"; }
        catch (Exception ex) { status.Text = "Не удалось сохранить: " + ex.Message; }
    }
}
