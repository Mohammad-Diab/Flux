using System.ComponentModel;
using Flux.Ui.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;

namespace FluxCast.Views;

/// <summary>The size warning pill in the title bar, shown while the presenter draws a frame below its
/// native size. The shell hosts it — it lives beside the caption buttons — and the presenter drives it,
/// feeding the flyout the numbers behind the warning.</summary>
public sealed partial class SizeWarningBadge : UserControl
{
    private const double WarningHeight = 28;   // capsule and circle share it, being one shape once
    private const double WarningCutRadius = 18;   // the circle's radius plus the gap it is set back by
    private const double WarningCutOffset = 6;    // how far past the label's edge that circle sits

    // The flyout's content is not built until it first opens, so the detail is held and applied there.
    private string _detail = "";
    private Storyboard? _pulse;

    public SizeWarningBadge()
    {
        InitializeComponent();
        MotionSettings.Current.PropertyChanged += OnMotionChanged;
        Unloaded += (_, _) =>
        {
            MotionSettings.Current.PropertyChanged -= OnMotionChanged;
            _pulse?.Stop();
        };
    }

    /// <summary>Shows the pill, or just refreshes the numbers behind it if already showing.</summary>
    public void Show(string detail)
    {
        _detail = detail;
        if (Visibility == Visibility.Visible)
            return;

        Visibility = Visibility.Visible;
        UpdatePulse();
    }

    public void Hide()
    {
        if (Visibility == Visibility.Collapsed)
            return;

        WarningButton.Flyout?.Hide();
        Visibility = Visibility.Collapsed;
        UpdatePulse();
    }

    private void OnMotionChanged(object? sender, PropertyChangedEventArgs e) => UpdatePulse();

    private void OnCapsuleResized(object sender, SizeChangedEventArgs e) => BuildShape();

    private void OnOpening(object sender, object e)
    {
        if (Detail is not null)
            Detail.Text = _detail;
    }

    private void OnDismiss(object sender, RoutedEventArgs e) => WarningButton.Flyout?.Hide();

    /// <summary>
    /// Draws the label's outline: rounded at the left, and on the right a concave arc struck by the
    /// same circle the mark is, so the two look like one shape pulled apart.
    /// </summary>
    private void BuildShape()
    {
        double width = Capsule.ActualWidth, radius = WarningHeight / 2;
        if (width <= radius * 2)
            return;

        double centreX = width + WarningCutOffset;
        double edgeX = centreX - Math.Sqrt(WarningCutRadius * WarningCutRadius - radius * radius);

        var outline = new PathFigure { StartPoint = new Point(radius, 0), IsClosed = true, IsFilled = true };
        outline.Segments.Add(new LineSegment { Point = new Point(edgeX, 0) });
        outline.Segments.Add(new ArcSegment
        {
            Point = new Point(edgeX, WarningHeight),
            Size = new Size(WarningCutRadius, WarningCutRadius),
            SweepDirection = SweepDirection.Counterclockwise,
        });
        outline.Segments.Add(new LineSegment { Point = new Point(radius, WarningHeight) });
        outline.Segments.Add(new ArcSegment
        {
            Point = new Point(radius, 0),
            Size = new Size(radius, radius),
            SweepDirection = SweepDirection.Clockwise,
        });

        var geometry = new PathGeometry();
        geometry.Figures.Add(outline);
        Shape.Data = geometry;
    }

    /// <summary>
    /// Pulses the warning so it is noticed in the corner of the eye, and rests between beats so it is
    /// not a distraction during a cast. Scale is a render transform, so it needs no dependent
    /// animation, and the whole thing is off when motion is.
    /// </summary>
    private void UpdatePulse()
    {
        _pulse?.Stop();
        _pulse = null;
        MarkScale.ScaleX = MarkScale.ScaleY = 1;

        if (Visibility != Visibility.Visible || !MotionSettings.Current.AnimationsEnabled)
            return;

        var board = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
        foreach (var axis in new[] { "ScaleX", "ScaleY" })
        {
            var beat = new DoubleAnimationUsingKeyFrames();
            beat.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.Zero, Value = 1 });
            beat.KeyFrames.Add(new EasingDoubleKeyFrame
            {
                KeyTime = TimeSpan.FromMilliseconds(420), Value = 1.06, EasingFunction = MotionCurves.Travel,
            });
            beat.KeyFrames.Add(new EasingDoubleKeyFrame
            {
                KeyTime = TimeSpan.FromMilliseconds(840), Value = 1, EasingFunction = MotionCurves.Travel,
            });
            beat.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(3000), Value = 1 });
            Storyboard.SetTarget(beat, MarkScale);
            Storyboard.SetTargetProperty(beat, axis);
            board.Children.Add(beat);
        }

        _pulse = board;
        board.Begin();
    }
}
