using System.ComponentModel;
using Flux.Ui.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;

namespace FluxCast.Views;

/// <summary>The size warning pill in the title bar, shown while the presenter draws a frame below
/// its native size. The shell hosts it; the presenter drives it and feeds the flyout its numbers.</summary>
public sealed partial class SizeWarningBadge : UserControl
{
    private const double WarningHeight = 28;   // capsule and circle share it, being one shape once
    private const double WarningCutRadius = 18;   // the circle's radius plus the gap it is set back by
    private const double WarningCutOffset = 6;    // how far past the label's edge that circle sits

    // The flyout's content is not built until it first opens, so the detail is held and applied there.
    private string _detail = "";
    private Storyboard? _pulse;
    private Storyboard? _transition;
    private bool _shown;   // logical state; Visibility lags it while the exit plays

    public SizeWarningBadge()
    {
        InitializeComponent();
        MotionSettings.Current.PropertyChanged += OnMotionChanged;
        Unloaded += (_, _) =>
        {
            MotionSettings.Current.PropertyChanged -= OnMotionChanged;
            _pulse?.Stop();
            _transition?.Stop();
        };
    }

    /// <summary>Shows the pill, or just refreshes the numbers behind it if already showing.</summary>
    public void Show(string detail)
    {
        _detail = detail;
        if (_shown)
            return;

        _shown = true;
        StopBoards();
        bool fresh = Visibility == Visibility.Collapsed;
        Visibility = Visibility.Visible;

        if (!MotionSettings.Current.AnimationsEnabled)
        {
            SetPose(mark: 1, label: 1, text: 1);
            UpdatePulse();
            return;
        }

        // The mark lands first and pulls the label out of itself; the text arrives once there is room.
        // The label recoils inward rather than overshooting: past 1 it leaves its box and gets clipped.
        if (fresh)
            SetPose(mark: 0, label: 0, text: 0);
        var board = new Storyboard();
        Animate(board, MarkScale, "ScaleX", (300, 1, MotionCurves.Pop));
        Animate(board, MarkScale, "ScaleY", (300, 1, MotionCurves.Pop));
        Animate(board, CapsuleScale, "ScaleX",
            (140, 0, MotionCurves.Exit), (420, 1, MotionCurves.Settle),
            (520, 0.96, MotionCurves.Travel), (620, 1, MotionCurves.Settle));
        Animate(board, Summary, "Opacity", (300, 0, null), (500, 1, MotionCurves.Settle));
        board.Completed += (_, _) =>
        {
            if (!_shown)
                return;
            StopBoards();
            SetPose(mark: 1, label: 1, text: 1);
            UpdatePulse();
        };
        _transition = board;
        board.Begin();
    }

    public void Hide()
    {
        if (!_shown)
            return;

        _shown = false;
        WarningButton.Flyout?.Hide();
        StopBoards();

        if (!MotionSettings.Current.AnimationsEnabled)
        {
            Visibility = Visibility.Collapsed;
            SetPose(mark: 1, label: 1, text: 1);
            return;
        }

        // The label is swallowed back, the mark swells for the gulp, then pops away.
        var board = new Storyboard();
        Animate(board, Summary, "Opacity", (90, 0, MotionCurves.Exit));
        Animate(board, CapsuleScale, "ScaleX", (280, 0, MotionCurves.Exit));
        Animate(board, MarkScale, "ScaleX", (200, 1, null), (260, 1.12, MotionCurves.Travel), (380, 0, MotionCurves.Exit));
        Animate(board, MarkScale, "ScaleY", (200, 1, null), (260, 1.12, MotionCurves.Travel), (380, 0, MotionCurves.Exit));
        board.Completed += (_, _) =>
        {
            if (_shown)
                return;
            StopBoards();
            Visibility = Visibility.Collapsed;
            SetPose(mark: 1, label: 1, text: 1);
        };
        _transition = board;
        board.Begin();
    }

    private void StopBoards()
    {
        _transition?.Stop();
        _transition = null;
        _pulse?.Stop();
        _pulse = null;
    }

    // A stopped storyboard reverts to these local values, so every rest state is set through here.
    private void SetPose(double mark, double label, double text)
    {
        MarkScale.ScaleX = MarkScale.ScaleY = mark;
        CapsuleScale.ScaleX = label;
        Summary.Opacity = text;
    }

    private static void Animate(Storyboard board, DependencyObject target, string property,
        params (int At, double Value, EasingFunctionBase? Ease)[] keys)
    {
        var frames = new DoubleAnimationUsingKeyFrames();
        foreach (var (at, value, ease) in keys)
        {
            frames.KeyFrames.Add(new EasingDoubleKeyFrame
            {
                KeyTime = TimeSpan.FromMilliseconds(at), Value = value, EasingFunction = ease,
            });
        }

        Storyboard.SetTarget(frames, target);
        Storyboard.SetTargetProperty(frames, property);
        board.Children.Add(frames);
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
        // Inset by half the stroke: on the edge itself, the straight runs are clipped thinner than the arcs.
        double half = Shape.StrokeThickness / 2;
        double width = Capsule.ActualWidth, capX = WarningHeight / 2, radius = WarningHeight / 2 - half;
        if (width <= WarningHeight)
            return;

        double top = half, bottom = WarningHeight - half;
        double centreX = width + WarningCutOffset;
        double edgeX = centreX - Math.Sqrt(WarningCutRadius * WarningCutRadius - radius * radius);

        var outline = new PathFigure { StartPoint = new Point(capX, top), IsClosed = true, IsFilled = true };
        outline.Segments.Add(new LineSegment { Point = new Point(edgeX, top) });
        outline.Segments.Add(new ArcSegment
        {
            Point = new Point(edgeX, bottom),
            Size = new Size(WarningCutRadius, WarningCutRadius),
            SweepDirection = SweepDirection.Counterclockwise,
        });
        outline.Segments.Add(new LineSegment { Point = new Point(capX, bottom) });
        outline.Segments.Add(new ArcSegment
        {
            Point = new Point(capX, top),
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
            Animate(board, MarkScale, axis,
                (0, 1, null), (420, 1.06, MotionCurves.Travel), (840, 1, MotionCurves.Travel), (3000, 1, null));
        }

        _pulse = board;
        board.Begin();
    }
}
