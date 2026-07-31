using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;

namespace Flux.Ui.Controls;

/// <summary>Which way the genie plays: out of the trigger, or into it.</summary>
public enum GenieMode { None, Opening, Closing }

/// <summary>
/// Content host that plays a page transition when <see cref="Page"/> changes. Tab and in-page
/// navigation cross-fade with a slide (and an optional step back); when <see cref="Genie"/> is set the
/// page instead folds into — or pours out of — <see cref="GenieTarget"/> on a strip mesh, the
/// "magic lamp". Snaps instantly when <see cref="MotionSettings"/> is off.
/// </summary>
public class TransitionHost : Grid
{
    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(300);
    private const double ZoomOut = 0.94;

    // Genie ("magic lamp") — the strip warp toward the trigger, ported from the WPF control.
    private static readonly TimeSpan GenieOpen = TimeSpan.FromMilliseconds(340);
    private static readonly TimeSpan GenieClose = TimeSpan.FromMilliseconds(280);
    private const double GenieStagger = 0.55;    // how strongly trigger-side strips lead (taffy stretch)
    private const double GenieStripPx = 6;       // target strip width; thinner = smoother curve
    private const int GenieMaxStrips = 120;
    private const double GenieBendPhase = 0.45;  // portion of the timeline the funnel bend ramps in over
    private const double GenieNeckRatio = 0.06;  // strip height at the neck, as a fraction of full
    private const double GenieAboveContent = 74; // the trigger sits this far above the content top
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(16);

    private readonly ContentPresenter _incoming = new();
    private readonly ContentPresenter _outgoing = new() { IsHitTestVisible = false, Opacity = 0 };
    private readonly ScaleTransform _incomingScale = new() { CenterX = 0.5, CenterY = 0.5 };
    private readonly ScaleTransform _outgoingScale = new() { CenterX = 0.5, CenterY = 0.5 };
    private readonly TranslateTransform _incomingSlide = new();
    private readonly TranslateTransform _outgoingSlide = new();
    private readonly Canvas _genieLayer = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly List<Strip> _strips = [];
    private Storyboard? _running;
    private DispatcherTimer? _genieTimer;
    private double _genieWidth, _genieHeight, _genieTargetX, _genieTargetY;
    private int _generation;

    private sealed record Strip(double Left, double Width, ScaleTransform Scale, TranslateTransform Translate, UIElement Element);

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
            // RenderTargetBitmap captures an element's drawn bounds, not its layout slot, so a page
            // whose content is inset by a margin would be snapshotted cropped and stretched to fill.
            // A brush over the whole presenter makes those bounds the slot.
            presenter.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            presenter.RenderTransformOrigin = new Point(0.5, 0.5);
            presenter.RenderTransform = new TransformGroup { Children = { scale, slide } };
            Children.Add(presenter);
        }

        Children.Add(_genieLayer);

        // A resize invalidates the captured sheet, so drop the warp and show the real page.
        SizeChanged += (_, _) =>
        {
            if (_strips.Count > 0)
                AbortGenie();
        };
    }

    /// <summary>Drops a running warp and puts the live page back, whatever state it was left in.</summary>
    private void AbortGenie()
    {
        _generation++;
        _genieTimer?.Stop();
        _genieTimer = null;
        ClearStrips();
        _incoming.Opacity = 1;
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

    /// <summary>Set before changing <see cref="Page"/> to play the genie instead of the slide.</summary>
    public static readonly DependencyProperty GenieProperty = DependencyProperty.Register(
        nameof(Genie), typeof(GenieMode), typeof(TransitionHost), new PropertyMetadata(GenieMode.None));

    public GenieMode Genie
    {
        get => (GenieMode)GetValue(GenieProperty);
        set => SetValue(GenieProperty, value);
    }

    /// <summary>The point the genie funnels to or from — the trigger's centre, in this host's coords.</summary>
    public static readonly DependencyProperty GenieTargetProperty = DependencyProperty.Register(
        nameof(GenieTarget), typeof(Point), typeof(TransitionHost),
        new PropertyMetadata(new Point(double.NaN, double.NaN)));

    public Point GenieTarget
    {
        get => (Point)GetValue(GenieTargetProperty);
        set => SetValue(GenieTargetProperty, value);
    }

    private void Swap(object? previous, object? next)
    {
        _running?.Stop();
        _running = null;
        StopGenie();

        // A page lives in one tree at a time, so it must leave this presenter before the other shows it.
        _incoming.Content = null;

        if (previous is null || !MotionSettings.Current.AnimationsEnabled)
        {
            _outgoing.Content = null;
            _incoming.Content = next;
            Reset();
            return;
        }

        if (Genie != GenieMode.None && ActualWidth > 0 && ActualHeight > 0)
        {
            _ = PlayGenieAsync(Genie, previous, next, ++_generation);
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

    // Closing clones the outgoing page and sucks it into the trigger over the destination; Opening
    // renders the incoming page, hides it, and replays the warp in reverse.
    private async Task PlayGenieAsync(GenieMode mode, object? previous, object? next, int generation)
    {
        Reset();

        if (mode == GenieMode.Closing)
        {
            _outgoing.Content = previous;
            _incoming.Content = next;
            _outgoing.Opacity = 1;

            var shot = await SnapshotAsync(_outgoing);
            if (generation != _generation)
                return;

            _outgoing.Opacity = 0;
            _outgoing.Content = null;
            if (shot is not null && BuildStrips(shot.Value.Image, shot.Value.Width, shot.Value.Height))
                RunGenie(0, 1, GenieClose, generation);
            else
                AbortGenie();
            return;
        }

        _incoming.Content = next;
        var opening = await SnapshotAsync(_incoming);
        if (generation != _generation)
            return;

        _outgoing.Content = null;
        if (opening is null || !BuildStrips(opening.Value.Image, opening.Value.Width, opening.Value.Height))
        {
            _incoming.Opacity = 1;
            return;
        }

        _incoming.Opacity = 0;
        RunGenie(1, 0, GenieOpen, generation, revealIncoming: true);
    }

    private void RunGenie(double from, double to, TimeSpan duration, int generation, bool revealIncoming = false)
    {
        ApplyGenieFrame(from);

        // WinUI cannot animate a plain double into a callback, so the warp is stepped by hand — the
        // same 16 ms tween the mini capture window uses for its resize.
        var clock = Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = FrameInterval };
        timer.Tick += (_, _) =>
        {
            if (generation != _generation)
            {
                timer.Stop();
                return;   // a newer Swap already cleared the strips and reset the presenters
            }

            double t = Math.Clamp(clock.Elapsed.TotalMilliseconds / duration.TotalMilliseconds, 0, 1);
            ApplyGenieFrame(from + (to - from) * t);

            if (t < 1)
                return;

            timer.Stop();
            ClearStrips();
            if (revealIncoming)
                _incoming.Opacity = 1;
        };
        _genieTimer = timer;
        timer.Start();
    }

    private bool BuildStrips(ImageSource snapshot, double width, double height)
    {
        ClearStrips();

        // The sheet is laid out at the size it was captured at: re-reading ActualWidth here would
        // stretch a snapshot taken before a resize, which shows up as a squashed, shifted page.
        _genieWidth = width;
        _genieHeight = height;
        if (_genieWidth <= 0 || _genieHeight <= 0 || Math.Abs(ActualWidth - width) > 1
            || Math.Abs(ActualHeight - height) > 1)
            return false;

        // Strips travel toward the trigger, which sits above the content — keep them off the chrome.
        _genieLayer.Clip = new RectangleGeometry { Rect = new Rect(0, 0, _genieWidth, _genieHeight) };

        var target = GenieTarget;
        if (!double.IsNaN(target.X) && !double.IsNaN(target.Y))
        {
            _genieTargetX = target.X;
            _genieTargetY = target.Y;
        }
        else
        {
            _genieTargetX = _genieWidth - 160;
            _genieTargetY = -GenieAboveContent;
        }

        int count = (int)Math.Round(Math.Clamp(_genieWidth / GenieStripPx, 16, GenieMaxStrips));
        double stripW = _genieWidth / count;
        var background = Application.Current.Resources.TryGetValue("BgBrush", out var brush) ? brush as Brush : null;

        for (int i = 0; i < count; i++)
        {
            double left = i * stripW;
            double drawWidth = stripW + 0.75;   // slight overdraw hides sub-pixel seams between strips
            var scale = new ScaleTransform();
            var translate = new TranslateTransform();

            // WinUI brushes have no viewbox, so each strip is the whole page shifted left and clipped.
            var sheet = new Grid { Width = _genieWidth, Height = _genieHeight };
            if (background is not null)
                sheet.Children.Add(new Border { Background = background });
            sheet.Children.Add(new Image
            {
                Source = snapshot,
                Width = _genieWidth,
                Height = _genieHeight,
                Stretch = Stretch.Fill,
            });
            sheet.RenderTransform = new TranslateTransform { X = -left };

            var strip = new Grid
            {
                Width = drawWidth,
                Height = _genieHeight,
                Clip = new RectangleGeometry { Rect = new Rect(0, 0, drawWidth, _genieHeight) },
                RenderTransform = new TransformGroup { Children = { scale, translate } },
                Children = { sheet },
            };

            Canvas.SetLeft(strip, left);
            _genieLayer.Children.Add(strip);
            _strips.Add(new Strip(left, stripW, scale, translate, strip));
        }

        _genieLayer.Visibility = Visibility.Visible;
        return true;
    }

    // p: 0 fully open, 1 fully sucked in. Strips converge on the target (trigger-side strips leading,
    // for the taffy neck) and squeeze toward it, tracing a funnel through the page.
    private void ApplyGenieFrame(double p)
    {
        if (_strips.Count == 0)
            return;

        bool triggerRight = _genieTargetX > _genieWidth / 2;
        double bend = EaseInOutCubic(Clamp01(p / GenieBendPhase));

        foreach (var strip in _strips)
        {
            double center = strip.Left + strip.Width / 2;
            double u = triggerRight ? (_genieWidth - center) / _genieWidth : center / _genieWidth;
            double travel = Sq(Clamp01(p * (1 + GenieStagger) - u * GenieStagger));

            double warp = bend * travel * travel;
            double scaleY = 1 - (1 - GenieNeckRatio) * warp;
            double centerY = _genieHeight / 2 + (_genieTargetY - _genieHeight / 2) * warp;

            strip.Scale.ScaleY = scaleY;
            strip.Translate.X = travel * (_genieTargetX - center);
            strip.Translate.Y = centerY - scaleY * _genieHeight / 2;
            strip.Element.Opacity = 1 - travel;
        }
    }

    private async Task<(ImageSource Image, double Width, double Height)?> SnapshotAsync(ContentPresenter presenter)
    {
        // The shell hides its tab strip in the same breath as it swaps the page, so this host is still
        // holding its pre-collapse size until the pending pass runs — which it would, mid-await. Settle
        // it first, or the sheet is captured at one size and laid out at another.
        UpdateLayout();

        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0)
            return null;

        var bitmap = new RenderTargetBitmap();
        try
        {
            // The arguments are the target surface in DIPs and the drawn bounds are scaled to fill it,
            // so anything but the element's own size distorts the sheet. With the presenter's brush
            // making those bounds the layout slot, asking for the slot renders 1:1.
            await bitmap.RenderAsync(presenter, (int)Math.Round(w), (int)Math.Round(h));
        }
        catch
        {
            return null;   // a page that cannot be rendered just falls back to no transition
        }
        return (bitmap, w, h);
    }

    private void StopGenie()
    {
        _genieTimer?.Stop();
        _genieTimer = null;
        ClearStrips();
        _incoming.Opacity = 1;
    }

    private void ClearStrips()
    {
        _genieLayer.Children.Clear();
        _genieLayer.Visibility = Visibility.Collapsed;
        _strips.Clear();
    }

    private static double Clamp01(double v) => Math.Clamp(v, 0, 1);
    private static double Sq(double v) => v * v;
    private static double EaseInOutCubic(double t) => t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;

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
