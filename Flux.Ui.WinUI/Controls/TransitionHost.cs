using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace Flux.Ui.WinUI.Controls;

/// <summary>
/// Content host that cross-fades between pages: the outgoing page steps back and slides away while
/// the incoming one slides in and settles. Snaps instantly when <see cref="MotionSettings"/> is off.
/// </summary>
public class TransitionHost : Grid
{
    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(300);
    private const double ZoomOut = 0.94;

    private readonly ContentPresenter _incoming = new();
    private readonly ContentPresenter _outgoing = new() { IsHitTestVisible = false, Opacity = 0 };
    private readonly ScaleTransform _incomingScale = new() { CenterX = 0.5, CenterY = 0.5 };
    private readonly ScaleTransform _outgoingScale = new() { CenterX = 0.5, CenterY = 0.5 };
    private readonly TranslateTransform _incomingSlide = new();
    private readonly TranslateTransform _outgoingSlide = new();
    private Storyboard? _running;

    public TransitionHost()
    {
        foreach (var (presenter, scale, slide) in new[]
                 {
                     (_outgoing, _outgoingScale, _outgoingSlide),
                     (_incoming, _incomingScale, _incomingSlide),
                 })
        {
            presenter.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            presenter.VerticalContentAlignment = VerticalAlignment.Stretch;
            presenter.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
            presenter.RenderTransform = new TransformGroup { Children = { scale, slide } };
            Children.Add(presenter);
        }
    }

    /// <summary>The page on show. Assigning a different value plays the transition.</summary>
    public static readonly DependencyProperty PageProperty = DependencyProperty.Register(
        nameof(Page), typeof(object), typeof(TransitionHost),
        new PropertyMetadata(null, (d, e) => ((TransitionHost)d).Swap(e.OldValue, e.NewValue)));

    public object? Page
    {
        get => GetValue(PageProperty);
        set => SetValue(PageProperty, value);
    }

    /// <summary>Horizontal offset the incoming page arrives from; negative comes from the left.</summary>
    public static readonly DependencyProperty SlideFromProperty = DependencyProperty.Register(
        nameof(SlideFrom), typeof(double), typeof(TransitionHost), new PropertyMetadata(36d));

    public double SlideFrom
    {
        get => (double)GetValue(SlideFromProperty);
        set => SetValue(SlideFromProperty, value);
    }

    /// <summary>When true the pages also step back slightly as they cross.</summary>
    public static readonly DependencyProperty ZoomSlideProperty = DependencyProperty.Register(
        nameof(ZoomSlide), typeof(bool), typeof(TransitionHost), new PropertyMetadata(true));

    public bool ZoomSlide
    {
        get => (bool)GetValue(ZoomSlideProperty);
        set => SetValue(ZoomSlideProperty, value);
    }

    private void Swap(object? previous, object? next)
    {
        _running?.Stop();
        _running = null;

        // A page lives in one tree at a time, so it must leave this presenter before the other shows it.
        _incoming.Content = null;

        if (previous is null || !MotionSettings.Current.AnimationsEnabled)
        {
            _outgoing.Content = null;
            _incoming.Content = next;
            Reset();
            return;
        }

        _outgoing.Content = previous;
        _incoming.Content = next;

        double from = SlideFrom;
        double zoom = ZoomSlide ? ZoomOut : 1;
        var board = new Storyboard();

        Add(board, _outgoingSlide, "X", 0, -from);
        Add(board, _outgoing, "Opacity", 1, 0);
        Add(board, _incomingSlide, "X", from, 0);
        Add(board, _incoming, "Opacity", 0, 1);

        if (ZoomSlide)
        {
            foreach (var axis in new[] { "ScaleX", "ScaleY" })
            {
                Add(board, _outgoingScale, axis, 1, zoom);
                Add(board, _incomingScale, axis, zoom, 1);
            }
        }

        board.Completed += (_, _) =>
        {
            _outgoing.Content = null;
            Reset();
        };
        _running = board;
        board.Begin();
    }

    private static void Add(Storyboard board, DependencyObject target, string property, double from, double to)
    {
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = Duration,
            EasingFunction = MotionCurves.Settle,
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        board.Children.Add(animation);
    }

    private void Reset()
    {
        _incoming.Opacity = 1;
        _outgoing.Opacity = 0;
        _incomingSlide.X = _outgoingSlide.X = 0;
        _incomingScale.ScaleX = _incomingScale.ScaleY = 1;
        _outgoingScale.ScaleX = _outgoingScale.ScaleY = 1;
    }
}
