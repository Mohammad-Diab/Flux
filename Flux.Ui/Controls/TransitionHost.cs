using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
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
    private static readonly TimeSpan GenieOpen = TimeSpan.FromMilliseconds(480);
    private static readonly TimeSpan GenieClose = TimeSpan.FromMilliseconds(400);
    private const double GenieStagger = 0.55;    // how strongly trigger-side strips lead (taffy stretch)
    private const double GenieStripPx = 6;       // target strip width; thinner = smoother curve
    private const int GenieMaxStrips = 120;
    private const double GenieBendPhase = 0.45;  // portion of the timeline the funnel bend ramps in over
    private const double GenieNeckRatio = 0.06;  // strip height at the neck, as a fraction of full
    private const double GenieAboveContent = 74; // the trigger sits this far above the content top

    // Z order during a genie: incoming page (0) < baking composite (0, later child) < backdrop
    // cover (1) < the page being left (2) < the warping strips (3).
    private const int CoverZ = 1;
    private const int LeavingPageZ = 2;
    private const int GenieLayerZ = 3;

    private readonly ContentPresenter _incoming = new();
    private readonly ContentPresenter _outgoing = new() { IsHitTestVisible = false, Opacity = 0 };
    private readonly ScaleTransform _incomingScale = new() { CenterX = 0.5, CenterY = 0.5 };
    private readonly ScaleTransform _outgoingScale = new() { CenterX = 0.5, CenterY = 0.5 };
    private readonly TranslateTransform _incomingSlide = new();
    private readonly TranslateTransform _outgoingSlide = new();
    private readonly Grid _cover = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly Canvas _genieLayer = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly List<Strip> _strips = [];
    private Storyboard? _running;
    private EventHandler<object>? _genieFrame;
    private double _genieWidth, _genieHeight, _genieTargetX, _genieTargetY;
    private double _genieLift;   // keeps the sheet glued to the screen when the chrome resizes the host
    private int _generation;
    private bool _expectingChromeResize;

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

        Canvas.SetZIndex(_cover, CoverZ);
        Canvas.SetZIndex(_genieLayer, GenieLayerZ);
        Children.Add(_cover);
        Children.Add(_genieLayer);

        // A resize invalidates the captured sheet, so drop the warp and show the real page —
        // except the resize the genie itself asked for by returning the chrome mid-warp.
        SizeChanged += (_, _) =>
        {
            if (!_expectingChromeResize && _strips.Count > 0)
                AbortGenie();
        };
    }

    private void DetachGenieFrame()
    {
        if (_genieFrame is null)
            return;
        CompositionTarget.Rendering -= _genieFrame;
        _genieFrame = null;
    }

    /// <summary>Drops a running warp and puts the live page back, whatever state it was left in.</summary>
    private void AbortGenie()
    {
        _generation++;
        DetachGenieFrame();
        ClearStrips();
        HideCover();
        Canvas.SetZIndex(_outgoing, 0);
        _outgoing.Opacity = 0;
        _outgoing.Content = null;
        _incoming.Opacity = 1;
        RaiseSettled();   // a warp abandoned by a resize must not leave the chrome waiting
    }

    /// <summary>
    /// Raised once a page change has finished animating, or straight away when it does not animate.
    /// A shell whose chrome changes with the page waits for this: restoring it up front snaps the tab
    /// strip back while the old page is still on screen, and the content jumps as the rows resize.
    /// </summary>
    public event EventHandler? Settled;

    /// <summary>
    /// Raised while a genie holds the screen frozen: opening once the page being left has bowed out,
    /// closing once its captured sheet is seated over the destination. Chrome that belongs to the
    /// destination page changes here — the warp hides the reflow, so nothing is seen jumping.
    /// </summary>
    public event EventHandler? GenieAnchored;

    private void RaiseSettled() => Settled?.Invoke(this, EventArgs.Empty);

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

    /// <summary>
    /// The shell's ambient background layer. The genie captures it so the sheet it warps carries the
    /// exact backdrop the live window shows — without it the sheet is a visibly flat rectangle.
    /// </summary>
    public static readonly DependencyProperty AmbientSourceProperty = DependencyProperty.Register(
        nameof(AmbientSource), typeof(UIElement), typeof(TransitionHost), new PropertyMetadata(null));

    public UIElement? AmbientSource
    {
        get => (UIElement?)GetValue(AmbientSourceProperty);
        set => SetValue(AmbientSourceProperty, value);
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
            RaiseSettled();
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
            RaiseSettled();
        };
        _running = board;
        board.Begin();
    }

    // Both directions freeze the screen first: the page being left stays on top over a backdrop
    // cover while the sheets are prepared beneath, so no intermediate state is ever presented.
    // Opening then bows the old page out, lets the chrome adopt the destination, and pours the new
    // page out of the trigger; Closing seats the captured sheet over the destination, returns the
    // chrome under it, and sucks the sheet in.
    private async Task PlayGenieAsync(GenieMode mode, object? previous, object? next, int generation)
    {
        Reset();

        _outgoing.Content = previous;
        _outgoing.Opacity = 1;
        Canvas.SetZIndex(_outgoing, LeavingPageZ);

        _incoming.Content = next;
        // Until the cover is opaque the destination would show through the old page's transparent
        // surface; hidden, the live window backdrop shows instead — exactly what was already there.
        _incoming.Opacity = 0;

        var ambient = await SnapshotAmbientAsync();
        if (generation != _generation)
            return;   // a newer Swap already reset the presenters

        FillCover(ambient);
        _incoming.Opacity = 1;   // behind the opaque cover now, where the bake can see it

        if (mode == GenieMode.Opening)
        {
            // The old page bows out before the chrome changes; the cover is pinned to the window,
            // so the strip row collapsing under it reflows nothing visible.
            _outgoing.Opacity = 0;
            _outgoing.Content = null;
            Canvas.SetZIndex(_outgoing, 0);
            GenieAnchored?.Invoke(this, EventArgs.Empty);
            UpdateLayout();
            RealignCover(ambient);

            var sheet = await SnapshotPageAsync(_incoming, generation);
            if (generation != _generation)
                return;

            if (sheet is not null && BuildStrips(sheet.Value.Image, ambient, sheet.Value.Width, sheet.Value.Height))
            {
                // Seat frame 1 — fully sucked in, invisible — before anything is uncovered.
                ApplyGenieFrame(1);
                _incoming.Opacity = 0;
                HideCover();
                RunGenie(1, 0, GenieOpen, generation, revealIncoming: true);
            }
            else
            {
                HideCover();
                RaiseSettled();
            }
            return;
        }

        var closing = await SnapshotPageAsync(_outgoing, generation);
        if (generation != _generation)
            return;

        if (closing is null || !BuildStrips(closing.Value.Image, ambient, closing.Value.Width, closing.Value.Height))
        {
            HideCover();
            Canvas.SetZIndex(_outgoing, 0);
            _outgoing.Opacity = 0;
            _outgoing.Content = null;
            RaiseSettled();
            return;
        }

        // The sheet at identity is pixel-identical to the page being left, so swapping them and
        // dropping the cover changes nothing on screen.
        ApplyGenieFrame(0);
        Canvas.SetZIndex(_outgoing, 0);
        _outgoing.Opacity = 0;
        _outgoing.Content = null;
        HideCover();

        // The chrome returns while the opaque sheet still fills the host; gluing the strip layer to
        // its screen position hides the host being pushed down by the returning strip row. The host
        // keeps its bottom edge (the window's), so the row it lost on top is exactly the height it
        // shrank by — sizes are fresh after UpdateLayout where TransformToVisual can still lag.
        double beforeHeight = ActualHeight;
        _expectingChromeResize = true;
        GenieAnchored?.Invoke(this, EventArgs.Empty);
        UpdateLayout();
        _expectingChromeResize = false;
        // Carried by every strip's own transform: a RenderTransform on the layer itself is not
        // reliably picked up once the warp is stepping the strip transforms each frame.
        _genieLift = beforeHeight - ActualHeight;
        ApplyGenieFrame(0);

        RunGenie(0, 1, GenieClose, generation);
    }

    private void RunGenie(double from, double to, TimeSpan duration, int generation, bool revealIncoming = false)
    {
        ApplyGenieFrame(from);

        // WinUI cannot animate a plain double into a callback, so the warp is stepped by hand. It steps
        // on CompositionTarget.Rendering, not a DispatcherTimer: a 16 ms timer drifts against the
        // refresh and lands two frames' worth of warp in one, which is the judder WPF never had.
        var clock = Stopwatch.StartNew();
        EventHandler<object>? onFrame = null;
        onFrame = (_, _) =>
        {
            if (generation != _generation)
            {
                CompositionTarget.Rendering -= onFrame;
                return;   // a newer Swap already cleared the strips and reset the presenters
            }

            double t = Math.Clamp(clock.Elapsed.TotalMilliseconds / duration.TotalMilliseconds, 0, 1);
            ApplyGenieFrame(from + (to - from) * t);

            if (t < 1)
                return;

            CompositionTarget.Rendering -= onFrame;
            _genieFrame = null;
            ClearStrips();
            if (revealIncoming)
                _incoming.Opacity = 1;
            RaiseSettled();
        };
        _genieFrame = onFrame;
        CompositionTarget.Rendering += onFrame;
    }

    private bool BuildStrips(ImageSource snapshot, ImageSource? ambient, double width, double height)
    {
        ClearStrips();

        // The sheet is laid out at the size it was captured at: re-reading ActualWidth here would
        // stretch a snapshot taken before a resize, which shows up as a squashed, shifted page.
        _genieWidth = width;
        _genieHeight = height;
        if (_genieWidth <= 0 || _genieHeight <= 0 || Math.Abs(ActualWidth - width) > 1
            || Math.Abs(ActualHeight - height) > 1)
            return false;

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

        // Strips sit on whole-physical-pixel boundaries so no edge lands mid-pixel and antialiases
        // into a hairline, and each overdraws its neighbour by a pixel so no gap opens as they
        // spread apart in flight. The sheet is opaque, so the overlap never double-blends.
        double raster = XamlRoot?.RasterizationScale ?? 1;
        double overdraw = 1 / raster;
        var edges = new double[count + 1];
        for (int i = 0; i <= count; i++)
            edges[i] = Math.Round(i * _genieWidth * raster / count) / raster;

        for (int i = 0; i < count; i++)
        {
            double left = edges[i];
            double stripW = edges[i + 1] - left;
            double drawWidth = Math.Min(stripW + overdraw, _genieWidth - left);
            var scale = new ScaleTransform();
            var translate = new TranslateTransform();

            // WinUI brushes have no viewbox, so each strip is the whole sheet shifted left and
            // clipped. The sheet carries the window backdrop live under the page image — flattening
            // them into one bitmap first is what shifted and squashed the page, because
            // RenderTargetBitmap renders the union of drawn bounds and the backdrop overflows.
            var sheet = new Grid { Width = _genieWidth, Height = _genieHeight };
            AddBackdropLayers(sheet, ambient);
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
            strip.Translate.Y = centerY - scaleY * _genieHeight / 2 - _genieLift;
            strip.Element.Opacity = 1 - travel;
        }
    }

    // ---- Backdrop reconstruction -------------------------------------------------------------

    // The pages paint no background of their own: the window root's BgBrush gradient plus the
    // drifting ambient orbs show through them. A sheet baked over anything else — the WPF port's
    // flat BgBrush respanned to the content area — reads as a pale rectangle the moment it moves.
    // These layers rebuild that exact backdrop: the theme's gradient at window size and alignment,
    // and a capture of the ambient layer, both pinned to the window rather than to this host.

    private FrameworkElement? Root => XamlRoot?.Content as FrameworkElement;

    private Point OriginInRoot() =>
        Root is { } root ? TransformToVisual(root).TransformPoint(new Point(0, 0)) : new Point(0, 0);

    /// <summary>BgBrush for the theme this host actually renders in — an app-resources lookup would
    /// resolve against the app default and paint the wrong theme's backdrop mid-animation.</summary>
    private Brush? ThemedBackground()
    {
        string themeKey = ActualTheme == ElementTheme.Dark ? "Dark" : "Light";
        return Find(Application.Current.Resources)
            ?? (Application.Current.Resources.TryGetValue("BgBrush", out var any) ? any as Brush : null);

        Brush? Find(ResourceDictionary dictionary)
        {
            if (dictionary.ThemeDictionaries.TryGetValue(themeKey, out var themed)
                && themed is ResourceDictionary dict
                && dict.TryGetValue("BgBrush", out var brush))
                return brush as Brush;
            foreach (var merged in dictionary.MergedDictionaries)
            {
                if (Find(merged) is { } found)
                    return found;
            }
            return null;
        }
    }

    private async Task<ImageSource?> SnapshotAmbientAsync()
    {
        if (AmbientSource is not FrameworkElement ambient || ambient.Visibility == Visibility.Collapsed
            || ambient.ActualWidth <= 0 || ambient.ActualHeight <= 0)
            return null;

        // The orbs hang over the edges, and RenderTargetBitmap captures drawn bounds, not the slot:
        // unclipped, the capture would come back oversized and misplace every orb. The window clips
        // these regions anyway, so the momentary clip changes nothing on screen.
        var restore = ambient.Clip;
        ambient.Clip = new RectangleGeometry { Rect = new Rect(0, 0, ambient.ActualWidth, ambient.ActualHeight) };
        var bitmap = new RenderTargetBitmap();
        try
        {
            double raster = XamlRoot?.RasterizationScale ?? 1;
            await bitmap.RenderAsync(ambient,
                (int)Math.Round(ambient.ActualWidth * raster), (int)Math.Round(ambient.ActualHeight * raster));
        }
        catch
        {
            return null;   // the sheet just loses the orbs, keeping the gradient
        }
        finally
        {
            ambient.Clip = restore;
        }
        return bitmap;
    }

    private void AddBackdropLayers(Panel panel, ImageSource? ambient)
    {
        if (Root is not { } root)
            return;

        var origin = OriginInRoot();
        double rootW = root.ActualWidth, rootH = root.ActualHeight;
        var pin = new Thickness(-origin.X, -origin.Y, 0, 0);

        if (ThemedBackground() is { } background)
        {
            panel.Children.Add(new Rectangle
            {
                Width = rootW,
                Height = rootH,
                Fill = background,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = pin,
            });
        }

        if (ambient is not null)
        {
            panel.Children.Add(new Image
            {
                Source = ambient,
                Width = rootW,
                Height = rootH,
                Stretch = Stretch.Fill,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = pin,
            });
        }
    }

    private void FillCover(ImageSource? ambient)
    {
        _cover.Children.Clear();
        _cover.Clip = new RectangleGeometry { Rect = new Rect(0, 0, ActualWidth, ActualHeight) };
        AddBackdropLayers(_cover, ambient);
        _cover.Visibility = Visibility.Visible;
    }

    // The chrome change moved this host, so the cover's window-pinned layers need new offsets.
    private void RealignCover(ImageSource? ambient) => FillCover(ambient);

    private void HideCover()
    {
        _cover.Children.Clear();
        _cover.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Captures the page alone, transparent over nothing. It is NOT composited over the backdrop
    /// here: RenderTargetBitmap renders the union of drawn bounds — window-pinned backdrop layers
    /// overflow the slot, Clip notwithstanding, and the flattened sheet came back shifted and
    /// squashed. The strips layer the backdrop live instead; their clips cut the overflow.
    /// </summary>
    private async Task<(ImageSource Image, double Width, double Height)?> SnapshotPageAsync(
        ContentPresenter presenter, int generation)
    {
        // The shell may have changed the chrome in the same breath as the page swap; settle the
        // pending pass or the sheet is captured at one size and laid out at another.
        UpdateLayout();

        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0)
            return null;

        // Captured at physical-pixel resolution: the DIP-sized surface the port asked for before
        // undershot by the display scale, and the stretched-back-up sheet is what showed as blur
        // and seam hairlines mid-warp. The presenter's transparent brush keeps drawn bounds = slot,
        // so scaling the slot uniformly is a pure resolution change.
        double raster = XamlRoot?.RasterizationScale ?? 1;
        var page = new RenderTargetBitmap();
        try
        {
            await page.RenderAsync(presenter, (int)Math.Round(w * raster), (int)Math.Round(h * raster));
        }
        catch
        {
            return null;   // a page that cannot be rendered just falls back to no transition
        }

        if (generation != _generation || Math.Abs(ActualWidth - w) > 1 || Math.Abs(ActualHeight - h) > 1)
            return null;

        return (page, w, h);
    }

    // -------------------------------------------------------------------------------------------

    private void StopGenie()
    {
        DetachGenieFrame();
        ClearStrips();
        HideCover();
        Canvas.SetZIndex(_outgoing, 0);
        _incoming.Opacity = 1;
    }

    private void ClearStrips()
    {
        _genieLayer.Children.Clear();
        _genieLayer.Visibility = Visibility.Collapsed;
        _genieLift = 0;
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
        Canvas.SetZIndex(_outgoing, 0);
        _incomingSlide.X = _outgoingSlide.X = 0;
        _incomingScale.ScaleX = _incomingScale.ScaleY = 1;
        _outgoingScale.ScaleX = _outgoingScale.ScaleY = 1;
    }
}
