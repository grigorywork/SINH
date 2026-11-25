using System.Windows;
using System.Windows.Media.Animation;
using MaterialDesignThemes.Wpf;

namespace Sinh.App;

internal static class WindowMotion
{
    internal static bool Enabled => SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast;
    private static readonly DependencyProperty ShownProperty = DependencyProperty.RegisterAttached("Shown", typeof(bool), typeof(WindowMotion), new PropertyMetadata(false));

    internal static void Apply(Window window)
    {
        ApplyPolicy(window);
        if ((bool)window.GetValue(ShownProperty)) return;
        window.SetValue(ShownProperty, true);
        if (window.Content is UIElement content) Reveal(content, 140);
    }

    internal static void ApplyPolicy(Window window)
    {
        TransitionAssist.SetDisableTransitions(window, !Enabled);
        RippleAssist.SetIsDisabled(window, !Enabled);
        if (!Enabled && window.Content is UIElement content) content.BeginAnimation(UIElement.OpacityProperty, null);
        if (window is EasterEggWindow egg) egg.RefreshMotion();
        if (window.DataContext is MainViewModel model) model.RefreshMotion();
    }

    internal static void Reveal(UIElement element, int milliseconds = 100)
    {
        if (!Enabled) return;
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(.88, element.Opacity, TimeSpan.FromMilliseconds(milliseconds))
        { FillBehavior = FillBehavior.Stop, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } }, HandoffBehavior.SnapshotAndReplace);
    }
}
