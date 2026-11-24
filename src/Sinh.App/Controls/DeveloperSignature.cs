using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Sinh.App;

// A visual interaction only: it never changes profiles, scans disks or writes settings.
public sealed class DeveloperSignature : UserControl
{
    public const string Developer = "MSS9LN";
    private static long activations;
    private static WeakReference<EasterEggWindow>? openEgg;
    private readonly TranslateTransform jump = new();

    public DeveloperSignature()
    {
        var caption = new TextBlock { Text = Developer, RenderTransform = jump, FontWeight = FontWeights.SemiBold };
        var button = new Button
        {
            Content = caption,
            Style = (Style)FindResource("MaterialDesignFlatButton"),
            MinHeight = 40,
            MinWidth = 96,
            Padding = new Thickness(12, 8, 12, 8),
            ToolTip = "Разработчик MSS9LN"
        };
        System.Windows.Automation.AutomationProperties.SetName(button, "Разработчик MSS9LN");
        button.Click += Activate;
        Content = button;
    }

    private void Activate(object sender, RoutedEventArgs e)
    {
        var count = Interlocked.Increment(ref activations);
        if (count % 2 == 0 && SystemParameters.ClientAreaAnimation)
        {
            var animation = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(280), FillBehavior = FillBehavior.Stop };
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(0)));
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(-9, KeyTime.FromPercent(.4), new CubicEase { EasingMode = EasingMode.EaseOut }));
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1), new BounceEase { Bounces = 1, Bounciness = 2, EasingMode = EasingMode.EaseOut }));
            jump.BeginAnimation(TranslateTransform.YProperty, animation, HandoffBehavior.SnapshotAndReplace);
        }
        if (count % 9 != 0) return;
        if (openEgg?.TryGetTarget(out var existing) == true && existing.IsVisible) { existing.Activate(); return; }
        var window = new EasterEggWindow { Owner = Window.GetWindow(this) };
        openEgg = new(window);
        window.Show();
    }
}
