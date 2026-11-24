using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Sinh.Settings;
using MaterialDesignThemes.Wpf;
using Theme = Sinh.Settings.Theme;

namespace Sinh.App;

internal sealed class AppearanceService : IDisposable
{
    private readonly SettingsStore store;
    public AppearanceService(SettingsStore store)
    {
        this.store = store; store.Changed += Changed;
        SystemEvents.UserPreferenceChanged += SystemChanged;
        System.Windows.Application.Current.Activated += Activated;
        Apply();
    }
    private void Activated(object? sender, EventArgs e) => Apply();
    private void Changed(object? sender, AppSettings e) => System.Windows.Application.Current.Dispatcher.InvokeAsync(Apply);
    private void SystemChanged(object sender, UserPreferenceChangedEventArgs e) => System.Windows.Application.Current.Dispatcher.InvokeAsync(Apply);
    private bool IsDark(Appearance a)
    {
        if (a.Theme != Theme.System) return a.Theme == Theme.Dark;
        return Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is 0;
    }
    private void Apply()
    {
        var app = System.Windows.Application.Current; var a = store.Current.Appearance; var dark = IsDark(a);
        app.Resources["Surface"] = Brush(dark ? "#171923" : "#F5F6FA");
        app.Resources["Panel"] = Brush(dark ? "#222533" : "#FFFFFF");
        app.Resources["Ink"] = Brush(dark ? "#F0F1F8" : "#202235");
        app.Resources["Muted"] = Brush(dark ? "#B2B6CA" : "#62667D");
        app.Resources["Line"] = Brush(dark ? "#42465C" : "#DDE0EA");
        var accent = a.Accent switch { "Purple" => "#8271E8", "Blue" => "#4584E5", "Green" => "#39966F", "Orange" => "#D38638", "Pink" => "#CC6B9C", "Teal" => "#299FAD", _ => null };
        app.Resources["Accent"] = accent != null ? Brush(accent) : SystemParameters.WindowGlassBrush;
        var palette = new MaterialDesignThemes.Wpf.PaletteHelper();
        var materialTheme = palette.GetTheme();
        materialTheme.SetBaseTheme(dark ? MaterialDesignThemes.Wpf.BaseTheme.Dark : MaterialDesignThemes.Wpf.BaseTheme.Light);
        materialTheme.SetPrimaryColor(((SolidColorBrush)app.Resources["Accent"]).Color);
        palette.SetTheme(materialTheme);
        foreach (Window window in app.Windows) ApplyWindow(window);
    }
    private void ApplyWindow(Window w)
    {
        var a = store.Current.Appearance;
        w.FontSize = 14 * a.Scale; // DPI scaling remains WPF's responsibility; hit targets stay at least 38 DIPs.
        try { w.FontFamily = new FontFamily(a.Font); } catch { w.FontFamily = new FontFamily("Segoe UI"); }
        var icon = a.Icon.ToString().ToLowerInvariant();
        w.Icon = BitmapFrame.Create(new Uri($"pack://application:,,,/Assets/{icon}.ico"));
        if (a.Glass)
        {
            var tint = a.Tint switch { "Blue" => "#729AD4", "Teal" => "#69BFBD", "Lilac" => "#B79BDE", "Pink" => "#DEA9CC", "Amber" => "#DCBE81", "Green" => "#8EC2A0", "Graphite" => "#7D8593", _ => IsDark(a) ? "#34334B" : "#E5E4F3" };
            var color = (Color)ColorConverter.ConvertFromString(tint);
            // Text is never blurred. This lightweight surface is the fallback on Windows 10 / unsupported GPUs.
            var baseColor = (Color)ColorConverter.ConvertFromString(IsDark(a) ? "#171923" : "#F5F6FA");
            var factor = (100 - a.Transparency) / 100 * .65;
            Color Mix(Color c) => Color.FromRgb((byte)(baseColor.R * (1 - factor) + c.R * factor), (byte)(baseColor.G * (1 - factor) + c.G * factor), (byte)(baseColor.B * (1 - factor) + c.B * factor));
            w.Background = a.Background == "Solid" ? new SolidColorBrush(Mix(color)) : new LinearGradientBrush(Mix(color), baseColor, a.Background == "Stripes" ? 0 : 35);
        }
        else w.SetResourceReference(Window.BackgroundProperty, "Surface");
        var handle = new WindowInteropHelper(w).Handle;
        if (handle != IntPtr.Zero)
        {
            var dark = IsDark(a) ? 1 : 0; NativeIdentity.DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
        }
    }
    private static SolidColorBrush Brush(string s) => new((Color)ColorConverter.ConvertFromString(s));
    public void Dispose() { store.Changed -= Changed; SystemEvents.UserPreferenceChanged -= SystemChanged; System.Windows.Application.Current.Activated -= Activated; }
}

internal static class NativeIdentity
{
    internal const string AppId = "SINH.FolderSync.Desktop";
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SetCurrentProcessExplicitAppUserModelID(string id);
    [DllImport("dwmapi.dll")] internal static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    internal static void SetIdentity() => SetCurrentProcessExplicitAppUserModelID(AppId);
}
