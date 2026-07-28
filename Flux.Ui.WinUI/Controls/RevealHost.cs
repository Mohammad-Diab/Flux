using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;

namespace Flux.Ui.WinUI.Controls;

/// <summary>Reveals or hides its content by animating height, so the layout reflows as it opens.
/// WinUI has no <c>LayoutTransform</c>, so the height itself animates.</summary>
public class RevealHost : ContentControl
{
    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(260);

    private double _openHeight;
    private Storyboard? _running;

    public RevealHost()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Top;
        ClipContent();
        SizeChanged += (_, _) => MeasureOpenHeight();
    }

    /// <summary>Whether the content is shown.</summary>
    public static readonly DependencyProperty IsOpenProperty = DependencyProperty.Register(
        nameof(IsOpen), typeof(bool), typeof(RevealHost),
        new PropertyMetadata(false, (d, _) => ((RevealHost)d).Apply(animate: true)));

    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var natural = base.MeasureOverride(new Size(availableSize.Width, double.PositiveInfinity));
        _openHeight = natural.Height;
        return IsOpen || _running is not null ? natural : new Size(natural.Width, 0);
    }

    private void MeasureOpenHeight()
    {
        if (IsOpen && _running is null && ActualHeight > 0)
            _openHeight = ActualHeight;
    }

    private void Apply(bool animate)
    {
        _running?.Stop();
        _running = null;

        if (!animate || !MotionSettings.Current.AnimationsEnabled || _openHeight <= 0)
        {
            Height = double.NaN;
            Visibility = IsOpen ? Visibility.Visible : Visibility.Collapsed;
            InvalidateMeasure();
            return;
        }

        Visibility = Visibility.Visible;
        var animation = new DoubleAnimation
        {
            From = IsOpen ? 0 : _openHeight,
            To = IsOpen ? _openHeight : 0,
            Duration = Duration,
            EasingFunction = MotionCurves.Settle,
            // Height is a layout property, so WinUI silently drops the animation without this flag.
            EnableDependentAnimation = true,
        };
        Storyboard.SetTarget(animation, this);
        Storyboard.SetTargetProperty(animation, "Height");

        var board = new Storyboard();
        board.Children.Add(animation);
        board.Completed += (_, _) =>
        {
            _running = null;
            Height = double.NaN;
            Visibility = IsOpen ? Visibility.Visible : Visibility.Collapsed;
            InvalidateMeasure();
        };
        _running = board;
        board.Begin();
    }

    private void ClipContent()
    {
        var clip = new Microsoft.UI.Xaml.Media.RectangleGeometry();
        SizeChanged += (_, e) => clip.Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height);
        Clip = clip;
    }
}
