using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Flux.Ui.Controls;

/// <summary>
/// A ContentControl that plays a fluid entrance transition (slide + settle-scale + fade with
/// strong ease-out) whenever its content changes — the media-center-style page transition.
/// Skips the animation when <see cref="MotionSettings"/> is off.
/// </summary>
public class TransitionHost : ContentControl
{
    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(380);

    /// <summary>
    /// Horizontal px the incoming content slides in from (positive = from the right); set before
    /// changing <see cref="ContentControl.Content"/> to make the slide directional.
    /// </summary>
    public double SlideFrom { get; set; } = 36;

    protected override void OnContentChanged(object oldContent, object newContent)
    {
        base.OnContentChanged(oldContent, newContent);

        if (newContent is null)
            return;

        if (!MotionSettings.Current.AnimationsEnabled)
        {
            RenderTransform = Transform.Identity;
            Opacity = 1;
            return;
        }

        var translate = new TranslateTransform(SlideFrom, 0);
        var scale = new ScaleTransform(0.985, 0.985);
        var group = new TransformGroup();
        group.Children.Add(scale);
        group.Children.Add(translate);
        RenderTransform = group;
        RenderTransformOrigin = new Point(0.5, 0.5);

        var ease = new QuinticEase { EasingMode = EasingMode.EaseOut };
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Duration) { EasingFunction = ease });
        translate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(SlideFrom, 0, Duration) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.985, 1, Duration) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.985, 1, Duration) { EasingFunction = ease });
    }
}
