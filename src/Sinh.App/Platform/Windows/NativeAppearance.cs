using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Sinh.Settings;

namespace Sinh.App;

internal static class NativeIdentity
{
    internal const string AppId = "SINH.FolderSync.Desktop";
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SetCurrentProcessExplicitAppUserModelID(string id);
    internal static void SetIdentity() => SetCurrentProcessExplicitAppUserModelID(AppId);
}

internal static class NativeAppearance
{
    [StructLayout(LayoutKind.Sequential)] private struct Margins { public int Left, Right, Top, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Rectangle { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public uint Size; public Rectangle Monitor, Work; public uint Flags; }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr window, ref Margins margins);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    internal static void Apply(Window window, Appearance appearance, bool dark)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        var darkValue = dark ? 1 : 0; DwmSetWindowAttribute(handle, 20, ref darkValue, sizeof(int));
        var nativeGlass = appearance.Glass && Environment.OSVersion.Version.Build >= 22621 && (RenderCapability.Tier >> 16) >= 2 && !SystemParameters.HighContrast;
        var backdrop = nativeGlass ? 2 : 1;
        nativeGlass = nativeGlass && DwmSetWindowAttribute(handle, 38, ref backdrop, sizeof(int)) >= 0;
        if (!nativeGlass) { backdrop = 1; DwmSetWindowAttribute(handle, 38, ref backdrop, sizeof(int)); }
        var value = nativeGlass ? -1 : 0; var margins = new Margins { Left = value, Right = value, Top = value, Bottom = value }; DwmExtendFrameIntoClientArea(handle, ref margins);
        if (PresentationSource.FromVisual(window) is HwndSource source) source.CompositionTarget.BackgroundColor = nativeGlass ? Colors.Transparent : (dark ? Color.FromRgb(23, 25, 35) : Color.FromRgb(245, 246, 250));
        window.SetResourceReference(Window.BackgroundProperty, nativeGlass ? "GlassSurface" : "WindowSurface");
    }
    internal static void FitInitialWindow(Window window)
    {
        var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(new WindowInteropHelper(window).Handle, 2), ref info)) return;
        var dpi = VisualTreeHelper.GetDpi(window); var maxWidth = (info.Work.Right - info.Work.Left) / dpi.DpiScaleX; var maxHeight = (info.Work.Bottom - info.Work.Top) / dpi.DpiScaleY;
        var width = window.Width; var height = window.Height;
        window.MinWidth = Math.Min(window.MinWidth, maxWidth); window.MinHeight = Math.Min(window.MinHeight, maxHeight);
        if (double.IsFinite(width) && width > maxWidth) { window.Width = maxWidth; window.Left += (width - maxWidth) / 2; }
        if (double.IsFinite(height) && height > maxHeight) { window.Height = maxHeight; window.Top += (height - maxHeight) / 2; }
    }
}
