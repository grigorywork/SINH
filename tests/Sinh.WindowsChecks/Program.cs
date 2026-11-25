using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using Sinh.App;
using Sinh.Settings;
using MaterialDesignThemes.Wpf;
using Theme = Sinh.Settings.Theme;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var app = new Sinh.App.App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.InitializeComponent();
        var task = RunAsync(app);
        var frame = new DispatcherFrame();
        _ = task.ContinueWith(_ => app.Dispatcher.Invoke(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
        try { return task.GetAwaiter().GetResult(); }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { app.Shutdown(); }
    }
    private static IEnumerable<T> Children<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T value) yield return value;
            foreach (var nested in Children<T>(child)) yield return nested;
        }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Capture(Window window, string name)
    {
        window.UpdateLayout();
        var image = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        image.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        System.IO.Directory.CreateDirectory("artifacts/windows-ui");
        using var file = System.IO.File.Create("artifacts/windows-ui/" + name + ".png"); encoder.Save(file);
    }
    private static async Task<int> RunAsync(Sinh.App.App app)
    {
        var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sinh-wpf-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(directory);
        Console.WriteLine(System.Runtime.InteropServices.RuntimeInformation.OSDescription + " / " + System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture);
        using var store = new SettingsStore(System.IO.Path.Combine(directory, "settings.json"));
        using var localization = new LocalizationService(store);
        using var appearance = new AppearanceService(store);
        var model = new MainViewModel(store);
        var window = new MainWindow(model); app.MainWindow = window;
        window.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Require(window.IsLoaded && window.IsVisible, "MainWindow did not load");
        Require(window.Icon != null, "Window icon missing");
        Require(window.FindResource("MaterialDesignOutlinedButton") is Style, "Material Design resources missing");
        Console.WriteLine("PASS WPF main XAML, icon and Material Design runtime resources");
        Require(TransitionAssist.GetDisableTransitions(window) == !WindowMotion.Enabled && RippleAssist.GetIsDisabled(window) == !WindowMotion.Enabled, "Reduced-motion policy did not reach Material Design controls");
        Capture(window, "main-ru");
        var settings = new SettingsWindow(store) { Owner = window }; settings.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        foreach (var expander in Children<Expander>(settings).ToArray()) expander.IsExpanded = true;
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Require(Children<Expander>(settings).All(e => e.Content != null), "Lazy settings panel failed");
        Require(Children<Image>(settings).Count(i => i.Width == 64 && i.Source is BitmapSource image && image.PixelWidth > 0) >= 4, "Icon previews are missing");
        Console.WriteLine("PASS lazy settings sections and four icon previews");
        Capture(settings, "settings-ru");
        GC.Collect(); GC.WaitForPendingFinalizers();
        foreach (var theme in new[] { Theme.Dark, Theme.Light, Theme.Dark })
        {
            await store.UpdateAsync(s => s with { Appearance = s.Appearance with { Theme = theme } });
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var color = ((SolidColorBrush)app.Resources["Surface"]).Color;
            Require(theme == Theme.Dark ? color.R < 80 : color.R > 180, "Theme observer lost after GC");
        }
        settings.Close();
        var recreated = new SettingsWindow(store) { Owner = window }; recreated.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); recreated.Close();
        Console.WriteLine("PASS theme updates after GC and settings-window recreation");
        foreach (var scale in new[] { .85, 1d, 1.15 })
        {
            await store.UpdateAsync(s => s with { Appearance = s.Appearance with { Scale = scale } }); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Require(Math.Abs((double)app.Resources["AppFontSize"] - 14 * scale) < .01, "Font scale resource did not update");
        }
        var original = store.Current.Appearance;
        var glass = new GlassWindow(original) { Owner = window }; glass.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Children<Slider>(glass).First().Value = 17; glass.Close();
        Require(glass.Result == null && store.Current.Appearance == original, "Closing glass draft saved settings");
        await store.UpdateAsync(s => s with { Appearance = s.Appearance with { Language = "en" } }); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Require(Children<Button>(window).Any(b => b.Content?.ToString() == "Compare"), "English captions did not update");
        Capture(window, "main-en-dark");
        Console.WriteLine("PASS interface font scales, discarded glass draft and English captions");
        var viewport = (ScrollViewer)window.FindName("LayoutViewport");
        var pairsList = (ListBox)window.FindName("PairsList");
        window.MinHeight = 300; window.Height = 420;
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
        viewport.ScrollToEnd(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Require(pairsList.ActualHeight >= 150 && !double.IsInfinity(pairsList.Height) && VirtualizingPanel.GetIsVirtualizing(pairsList), "Small window lost its bounded virtualized list");
        var footer = Children<DeveloperSignature>(window).First();
        var footerBounds = footer.TransformToAncestor(viewport).TransformBounds(new Rect(footer.RenderSize));
        Require(footerBounds.Bottom <= viewport.ActualHeight + 2, "Bottom actions cannot be scrolled into view");
        Capture(window, "main-small-large-font");
        window.Height = 740; window.MinHeight = 500; viewport.ScrollToHome();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Console.WriteLine("PASS small-window scrolling and finite virtualized list viewport");
        using (var cancelDialog = new CancellationTokenSource())
        {
            var dialog = new Window { Title = "Cancellation check", Width = 300, Height = 180 };
            _ = app.Dispatcher.InvokeAsync(cancelDialog.Cancel, DispatcherPriority.ContextIdle);
            Require(CancelableDialog.Show(dialog, window, cancelDialog.Token) != true && !dialog.IsVisible, "Cancellation left a modal approval window open");
        }
        Console.WriteLine("PASS cancellation closes a modal approval window");
        foreach (var font in FontCatalog.BuiltIns)
        {
            var family = FontCatalog.Resolve("builtin:" + font.Id);
            Require(family.GetTypefaces().Any(t => t.TryGetGlyphTypeface(out var glyph) && glyph.CharacterToGlyphMap.ContainsKey('Я') && glyph.CharacterToGlyphMap.ContainsKey('A')), "Embedded font lacks Cyrillic/Latin: " + font.Name);
        }
        Console.WriteLine("PASS ten embedded font families contain Cyrillic and Latin glyphs");
        var signature = Children<DeveloperSignature>(window).First();
        var button = Children<Button>(signature).First();
        Require(DeveloperSignature.Developer == "MSS9LN" && button.MinHeight >= 40, "Developer identity or hit area changed");
        for (var count = 1; count <= 8; count++)
        {
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Require(!app.Windows.OfType<EasterEggWindow>().Any(w => w.IsVisible), "Easter egg opened before ninth activation");
            if (count == 2 && WindowMotion.Enabled)
            {
                await Task.Delay(90);
                var text = (TextBlock)button.Content;
                Require(((TranslateTransform)text.RenderTransform).Y < 0, "Second activation did not bounce");
                await Task.Delay(250);
            }
        }
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var egg = app.Windows.OfType<EasterEggWindow>().Single(w => w.IsVisible);
        Require(egg.Owner == window && egg.Title.Contains("MSS9LN"), "Ninth-activation easter egg identity/owner incorrect");
        var source = PresentationSource.FromVisual(egg)!;
        egg.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        Require(!egg.IsVisible, "Escape did not close easter egg");
        Console.WriteLine("PASS MSS9LN activation counts, bounce policy, ninth activation and Escape");
        window.Close(); await store.FlushAsync();
        System.IO.Directory.Delete(directory, true);
        return 0;
    }
}
