using System.Windows.Media.Animation;

namespace Flux.Ui.Controls;

/// <summary>
/// The shared easing curves every Flux animation draws from, so motion reads as one system rather
/// than a per-control accident. Each is frozen and reused — easing functions are stateless, so one
/// instance can back any number of animations. Durations stay with their component: they are tuned
/// per interaction, and a shared duration scale would couple unrelated feels.
/// </summary>
public static class MotionCurves
{
    /// <summary>Something arriving or expanding, decelerating into place. The house default.</summary>
    public static readonly IEasingFunction Settle = Freeze(new QuinticEase { EasingMode = EasingMode.EaseOut });

    /// <summary>Something travelling between two places — symmetric ease in and out.</summary>
    public static readonly IEasingFunction Travel = Freeze(new CubicEase { EasingMode = EasingMode.EaseInOut });

    /// <summary>Something leaving, accelerating away.</summary>
    public static readonly IEasingFunction Exit = Freeze(new CubicEase { EasingMode = EasingMode.EaseIn });

    /// <summary>A surface popping into place with a slight overshoot (dialogs).</summary>
    public static readonly IEasingFunction Pop = Freeze(new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 });

    private static IEasingFunction Freeze(EasingFunctionBase ease)
    {
        ease.Freeze();
        return ease;
    }
}
