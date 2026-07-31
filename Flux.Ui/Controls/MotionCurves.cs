using Microsoft.UI.Xaml.Media.Animation;

namespace Flux.Ui.Controls;

/// <summary>
/// The shared easing curves every Flux animation draws from, so motion reads as one system rather
/// than a per-control accident. Durations stay with their component: they are tuned per interaction,
/// and a shared duration scale would couple unrelated feels.
/// </summary>
public static class MotionCurves
{
    /// <summary>Something arriving or expanding, decelerating into place. The house default.</summary>
    public static EasingFunctionBase Settle => new QuinticEase { EasingMode = EasingMode.EaseOut };

    /// <summary>Something travelling between two places — symmetric ease in and out.</summary>
    public static EasingFunctionBase Travel => new CubicEase { EasingMode = EasingMode.EaseInOut };

    /// <summary>Something leaving, accelerating away.</summary>
    public static EasingFunctionBase Exit => new CubicEase { EasingMode = EasingMode.EaseIn };

    /// <summary>A surface popping into place with a slight overshoot (dialogs).</summary>
    public static EasingFunctionBase Pop => new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 };
}
