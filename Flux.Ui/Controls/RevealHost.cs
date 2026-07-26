using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Flux.Ui.Controls;

/// <summary>
/// A ContentControl that reveals its content by growing and sliding it into place when
/// <see cref="IsOpen"/> turns true, and shrinks it away when it turns false. The grow/shrink is a
/// LayoutTransform scale so surrounding content reflows; the slide and fade are cosmetic. Snaps
/// instantly when <see cref="MotionSettings"/> is off.
/// </summary>
public class RevealHost : ContentControl
{
    private static readonly TimeSpan OpenDuration = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan CloseDuration = TimeSpan.FromMilliseconds(210);
    private const double SlideFrom = -10;

    private readonly ScaleTransform _scale = new(1, 0);
    private readonly TranslateTransform _slide = new(0, SlideFrom);

    public RevealHost()
    {
        LayoutTransform = _scale;
        RenderTransform = _slide;
        Opacity = 0;
        Visibility = Visibility.Collapsed;
    }

    /// <summary>When true the content grows/slides in; when false it shrinks away.</summary>
    public static readonly DependencyProperty IsOpenProperty = DependencyProperty.Register(
        nameof(IsOpen), typeof(bool), typeof(RevealHost), new PropertyMetadata(false, OnIsOpenChanged));

    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    private static void OnIsOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((RevealHost)d).Animate((bool)e.NewValue);

    private void Animate(bool open)
    {
        if (open)
            Visibility = Visibility.Visible;

        if (!MotionSettings.Current.AnimationsEnabled)
        {
            _scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            _slide.BeginAnimation(TranslateTransform.YProperty, null);
            BeginAnimation(OpacityProperty, null);
            _scale.ScaleY = open ? 1 : 0;
            _slide.Y = open ? 0 : SlideFrom;
            Opacity = open ? 1 : 0;
            Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            return;
        }

        var duration = open ? OpenDuration : CloseDuration;
        var ease = MotionCurves.Settle;

        var fade = new DoubleAnimation(open ? 1 : 0, duration) { EasingFunction = ease };
        if (!open)
            fade.Completed += (_, _) => { if (!IsOpen) Visibility = Visibility.Collapsed; };

        _scale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(open ? 1 : 0, duration) { EasingFunction = ease });
        _slide.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(open ? 0 : SlideFrom, duration) { EasingFunction = ease });
        BeginAnimation(OpacityProperty, fade);
    }
}
