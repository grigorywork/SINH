using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Sinh.App;

public partial class EasterEggWindow : Window
{
    public EasterEggWindow()
    {
        InitializeComponent();
        if (SystemParameters.ClientAreaAnimation)
        {
            var animation = new DoubleAnimation(1, 1.055, TimeSpan.FromMilliseconds(850)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
            Pulse.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
            Pulse.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
        }
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
        Closed += (_, _) => { Pulse.BeginAnimation(ScaleTransform.ScaleXProperty, null); Pulse.BeginAnimation(ScaleTransform.ScaleYProperty, null); };
    }
    private void CloseClick(object sender, RoutedEventArgs e) => Close();
}
