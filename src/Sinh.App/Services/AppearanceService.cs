using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Sinh.Settings;
using MaterialDesignThemes.Wpf;
using Theme = Sinh.Settings.Theme;

namespace Sinh.App;

internal sealed class AppearanceService : IDisposable
{
    private static WeakReference<AppearanceService>? active;
    private readonly SettingsStore store;
    private Appearance? applied;
    private bool appliedDark;
    private Color appliedAccent;
    private bool appliedHighContrast;

    static AppearanceService() => EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler(WindowLoaded));
    public AppearanceService(SettingsStore store)
    {
        this.store = store;
        active = new(this);
        store.Changed += Changed;
        SystemEvents.UserPreferenceChanged += SystemChanged;
        SystemParameters.StaticPropertyChanged += MotionChanged;
        Apply();
    }
    private static void WindowLoaded(object sender, RoutedEventArgs e)
    {
        if (active == null || !active.TryGetTarget(out var service)) return;
        var window = (Window)sender;
        // WPF implicit styles do not flow from Window to derived MainWindow/SettingsWindow types.
        if (window.ReadLocalValue(FrameworkElement.StyleProperty) == DependencyProperty.UnsetValue)
            window.SetResourceReference(FrameworkElement.StyleProperty, typeof(Window));
        if (!(bool)window.GetValue(DpiHookedProperty))
        {
            window.SetValue(DpiHookedProperty, true);
            window.DpiChanged += (_, _) => window.Dispatcher.InvokeAsync(() => NativeAppearance.FitInitialWindow(window));
        }
        NativeAppearance.Apply(window, service.store.Current.Appearance, service.IsDark(service.store.Current.Appearance));
        NativeAppearance.FitInitialWindow(window);
        WindowMotion.Apply(window);
    }
    private static readonly DependencyProperty DpiHookedProperty = DependencyProperty.RegisterAttached("DpiHooked", typeof(bool), typeof(AppearanceService), new PropertyMetadata(false));
    private void Changed(object? sender, AppSettings settings) => System.Windows.Application.Current.Dispatcher.InvokeAsync(Apply);
    private void SystemChanged(object sender, UserPreferenceChangedEventArgs e) => System.Windows.Application.Current.Dispatcher.InvokeAsync(Apply);
    private void MotionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(SystemParameters.ClientAreaAnimation) or nameof(SystemParameters.HighContrast))) return;
        System.Windows.Application.Current.Dispatcher.InvokeAsync(() => { Apply(); foreach (Window window in System.Windows.Application.Current.Windows) WindowMotion.ApplyPolicy(window); });
    }
    private bool IsDark(Appearance a) => a.Theme == Theme.System
        ? Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is 0
        : a.Theme == Theme.Dark;
    private void Apply()
    {
        var app = System.Windows.Application.Current;
        var a = store.Current.Appearance;
        var dark = IsDark(a);
        var accent = a.Accent switch
        {
            "Purple" => ColorOf("#8271E8"),
            "Blue" => ColorOf("#4584E5"),
            "Green" => ColorOf("#39966F"),
            "Orange" => ColorOf("#D38638"),
            "Pink" => ColorOf("#CC6B9C"),
            "Teal" => ColorOf("#299FAD"),
            _ => SystemParameters.WindowGlassColor
        };
        if (applied == a && appliedDark == dark && appliedAccent == accent && appliedHighContrast == SystemParameters.HighContrast) return;
        var surface = ColorOf(dark ? "#171923" : "#F5F6FA");
        app.Resources["Surface"] = Frozen(new SolidColorBrush(surface));
        app.Resources["Panel"] = Brush(dark ? "#222533" : "#FFFFFF");
        app.Resources["Ink"] = Brush(dark ? "#F0F1F8" : "#202235");
        app.Resources["Muted"] = Brush(dark ? "#B2B6CA" : "#62667D");
        app.Resources["Line"] = Brush(dark ? "#42465C" : "#DDE0EA");
        app.Resources["Accent"] = Frozen(new SolidColorBrush(accent));
        app.Resources["AppFontFamily"] = FontCatalog.Resolve(a.Font);
        app.Resources["AppFontSize"] = 14 * a.Scale;
        app.Resources["TitleFontSize"] = 32 * a.Scale;
        app.Resources["SectionFontSize"] = 20 * a.Scale;
        app.Resources["RowTitleFontSize"] = 18 * a.Scale;
        app.Resources["DialogTitleFontSize"] = 26 * a.Scale;
        app.Resources["DeveloperHeroFontSize"] = 28 * a.Scale;
        if (applied?.Icon != a.Icon) app.Resources["AppIcon"] = BitmapFrame.Create(AssetUris.For($"Assets/{a.Icon.ToString().ToLowerInvariant()}.ico"));
        var tint = Tint(a, dark);
        var density = Math.Clamp((100 - a.Transparency) / 100 * .65 + a.Frost / 100 * .2, 0, .9);
        var mixed = Color.FromRgb((byte)(surface.R * (1 - density) + tint.R * density), (byte)(surface.G * (1 - density) + tint.G * density), (byte)(surface.B * (1 - density) + tint.B * density));
        app.Resources["WindowSurface"] = a.Glass ? CreatePattern(a.Background, surface, mixed) : (Brush)app.Resources["Surface"];
        var alpha = (byte)Math.Clamp(255 * (100 - a.Transparency) / 100 + a.Frost * .35, 0, 255);
        app.Resources["GlassSurface"] = CreatePattern(a.Background, Color.FromArgb((byte)(alpha / 2), surface.R, surface.G, surface.B), Color.FromArgb(alpha, tint.R, tint.G, tint.B));
        var palette = new PaletteHelper();
        var theme = palette.GetTheme();
        theme.SetBaseTheme(dark ? BaseTheme.Dark : BaseTheme.Light);
        theme.SetPrimaryColor(accent);
        palette.SetTheme(theme);
        applied = a;
        appliedDark = dark;
        appliedAccent = accent;
        appliedHighContrast = SystemParameters.HighContrast;
        foreach (Window window in app.Windows) NativeAppearance.Apply(window, a, dark);
    }
    private static Color Tint(Appearance a, bool dark) => ColorOf(a.Tint switch
    {
        "Blue" => "#729AD4",
        "Teal" => "#69BFBD",
        "Lilac" => "#B79BDE",
        "Pink" => "#DEA9CC",
        "Amber" => "#DCBE81",
        "Green" => "#8EC2A0",
        "Graphite" => "#7D8593",
        _ => dark ? "#34334B" : "#E5E4F3"
    });
    internal static Brush CreatePattern(string pattern, Color background, Color tint)
    {
        if (pattern == "Solid") return Frozen(new SolidColorBrush(tint));
        if (pattern == "Stripes")
        {
            var drawing = new DrawingGroup();
            drawing.Children.Add(new GeometryDrawing(new SolidColorBrush(background), null, new RectangleGeometry(new Rect(0, 0, 64, 64))));
            drawing.Children.Add(new GeometryDrawing(new SolidColorBrush(tint), null, Geometry.Parse("M0,0 L32,0 L64,32 L64,64 Z M0,32 L32,64 L0,64 Z")));
            return Frozen(new DrawingBrush(drawing) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 64, 64), ViewportUnits = BrushMappingMode.Absolute, Stretch = Stretch.None });
        }
        return Frozen(new LinearGradientBrush(new GradientStopCollection { new(tint, 0), new(background, .55), new(tint, 1) }, 35));
    }
    private static Color ColorOf(string value) => (Color)ColorConverter.ConvertFromString(value);
    private static SolidColorBrush Brush(string value) => Frozen(new SolidColorBrush(ColorOf(value)));
    private static T Frozen<T>(T value) where T : Freezable { value.Freeze(); return value; }
    public void Dispose()
    {
        store.Changed -= Changed;
        SystemEvents.UserPreferenceChanged -= SystemChanged;
        SystemParameters.StaticPropertyChanged -= MotionChanged;
        if (active?.TryGetTarget(out var service) == true && ReferenceEquals(service, this)) active = null;
    }
}
