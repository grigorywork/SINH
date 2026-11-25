using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Sinh.App;

public partial class EasterEggWindow : Window
{
    private bool? animated;
    public EasterEggWindow()
    {
        InitializeComponent();
        RefreshMotion();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
        Closed += (_, _) => { Pulse.BeginAnimation(ScaleTransform.ScaleXProperty, null); Pulse.BeginAnimation(ScaleTransform.ScaleYProperty, null); };
    }
    internal void RefreshMotion()
    {
        if (animated == WindowMotion.Enabled) return;
        animated = WindowMotion.Enabled;
        Pulse.BeginAnimation(ScaleTransform.ScaleXProperty, null); Pulse.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        if (animated == true)
        {
            var animation = new DoubleAnimation(1, 1.055, TimeSpan.FromMilliseconds(850)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
            Pulse.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
            Pulse.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
        }
    }
    private void CloseClick(object sender, RoutedEventArgs e) => Close();
}
