using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FluxCast.Controls;

/// <summary>
/// Attached entrance animation: an element with a non-negative RiseDelay fades in and rises
/// into place when loaded, delayed by that many milliseconds — so sibling elements cascade
/// in sequence, media-center style. Replays whenever the element re-enters the visual tree.
/// </summary>
public static class Motion
{
    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(420);

    /// <summary>Identifies the Motion.RiseDelay attached property.</summary>
    public static readonly DependencyProperty RiseDelayProperty = DependencyProperty.RegisterAttached(
        "RiseDelay", typeof(int), typeof(Motion), new PropertyMetadata(-1, OnRiseDelayChanged));

    /// <summary>Gets the rise delay in milliseconds (-1 = no animation).</summary>
    /// <param name="element">Target element.</param>
    public static int GetRiseDelay(DependencyObject element) => (int)element.GetValue(RiseDelayProperty);

    /// <summary>Sets the rise delay in milliseconds.</summary>
    /// <param name="element">Target element.</param>
    /// <param name="value">Delay in milliseconds; -1 disables the animation.</param>
    public static void SetRiseDelay(DependencyObject element, int value) => element.SetValue(RiseDelayProperty, value);

    private static void OnRiseDelayChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element || (int)e.NewValue < 0)
            return;

        element.Loaded += (_, _) => Play(element, (int)e.NewValue);
    }

    private static void Play(FrameworkElement element, int delayMs)
    {
        var translate = new TranslateTransform(0, 18);
        element.RenderTransform = translate;
        element.Opacity = 0;

        var begin = TimeSpan.FromMilliseconds(delayMs);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        element.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, Duration) { BeginTime = begin, EasingFunction = ease });
        translate.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(18, 0, Duration) { BeginTime = begin, EasingFunction = ease });
    }
}
