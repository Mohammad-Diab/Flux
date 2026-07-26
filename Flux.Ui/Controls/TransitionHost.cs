using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Flux.Ui.Controls;

public enum GenieMode { None, Opening, Closing }

/// <summary>
/// A ContentControl that plays a page transition when its content changes. Tab switches (<see
/// cref="ZoomSlide"/>) use a sequenced zoom-slide-zoom (current page steps back, slides across, new
/// page steps forward); in-page navigation uses a plain slide. When <see cref="Genie"/> is set the
/// change instead plays a macOS-style genie: the outgoing page,
/// on a subdivided mesh, folds and is sucked toward the top-right (the Settings gear) with a curved
/// tapering neck, revealing the destination page beneath. Skips animation when
/// <see cref="MotionSettings"/> is off. Direction follows <see cref="SlideFrom"/>.
/// </summary>
public class TransitionHost : ContentControl
{
    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(480);
    private static readonly TimeSpan SlideDuration = TimeSpan.FromMilliseconds(300);   // plain in-page slide
    // Capture the transition sheets at ~1.25x max: a moving/warping page hides the detail, and a
    // smaller texture is far cheaper to composite each frame on a high-DPI display.
    private const double SnapshotMaxScale = 1.25;
    private const double ZoomOut = 0.82;   // how far the pages step back (scale) while they slide
    private const double ZoomEnd = 0.32;   // fraction of the timeline spent zooming out
    private const double SlideEnd = 0.68;   // fraction by which the slide is done (zoom-in fills the rest)

    // Genie ("magic lamp") — a strip-clone warp toward the top-right Settings gear. Ported from the
    // CSS/React implementation: p 0 (open) → 1 (hidden); opening replays it in reverse.
    private static readonly TimeSpan GenieOpen = TimeSpan.FromMilliseconds(340);
    private static readonly TimeSpan GenieClose = TimeSpan.FromMilliseconds(280);
    private const double GenieStagger = 0.55;   // how strongly trigger-side strips lead (taffy stretch)
    private const double GenieStripPx = 6;      // target strip width; thinner = smoother curve, more layers
    private const int GenieMaxStrips = 120;
    private const double GenieBendPhase = 0.45; // portion of the timeline the funnel bend ramps in over
    private const double GenieNeckRatio = 0.06; // strip height at the neck, as a fraction of full
    private const double GenieAboveContent = 74; // the gear/back sits this far above the content top

    private ContentPresenter? _presenter;
    private Image? _outgoing;
    private ScaleTransform? _incomingScale;
    private TranslateTransform? _incomingSlide;
    private ScaleTransform? _outgoingScale;
    private TranslateTransform? _outgoingSlide;

    private Canvas? _genieLayer;
    private readonly List<Strip> _strips = new();
    private double _genieWidth, _genieHeight, _genieTargetX, _genieTargetY;
    private int _generation;

    private sealed class Strip
    {
        public required double Left;
        public required double Width;
        public required ScaleTransform Scale;
        public required TranslateTransform Translate;
        public required UIElement Element;
    }

    static TransitionHost()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(TransitionHost), new FrameworkPropertyMetadata(typeof(TransitionHost)));
    }

    /// <summary>
    /// Sign selects the direction; set before changing <see cref="ContentControl.Content"/> to make
    /// the transition directional.
    /// </summary>
    public static readonly DependencyProperty SlideFromProperty =
        DependencyProperty.Register(nameof(SlideFrom), typeof(double), typeof(TransitionHost), new PropertyMetadata(36.0));

    public double SlideFrom
    {
        get => (double)GetValue(SlideFromProperty);
        set => SetValue(SlideFromProperty, value);
    }

    /// <summary>When true the change plays the zoom-slide-zoom (tab switches); when false it plays a
    /// plain slide (in-page navigation). Ignored when a genie is requested.</summary>
    public static readonly DependencyProperty ZoomSlideProperty =
        DependencyProperty.Register(nameof(ZoomSlide), typeof(bool), typeof(TransitionHost), new PropertyMetadata(false));

    public bool ZoomSlide
    {
        get => (bool)GetValue(ZoomSlideProperty);
        set => SetValue(ZoomSlideProperty, value);
    }

    /// <summary>
    /// When set (non-None), the next content change plays the genie instead of the tab transition:
    /// Opening pours the new page out of the gear, Closing sucks the old page into it. Set before
    /// changing <see cref="ContentControl.Content"/>.
    /// </summary>
    public static readonly DependencyProperty GenieProperty =
        DependencyProperty.Register(nameof(Genie), typeof(GenieMode), typeof(TransitionHost), new PropertyMetadata(GenieMode.None));

    public GenieMode Genie
    {
        get => (GenieMode)GetValue(GenieProperty);
        set => SetValue(GenieProperty, value);
    }

    /// <summary>Exact point the genie funnels to/from (the gear button), in this control's coords.</summary>
    public static readonly DependencyProperty GenieTargetProperty =
        DependencyProperty.Register(nameof(GenieTarget), typeof(Point), typeof(TransitionHost),
            new PropertyMetadata(new Point(double.NaN, double.NaN)));

    public Point GenieTarget
    {
        get => (Point)GetValue(GenieTargetProperty);
        set => SetValue(GenieTargetProperty, value);
    }

    // Drives the genie warp (0 = fully open, 1 = fully sucked in); each step re-lays the strips.
    public static readonly DependencyProperty GenieProgressProperty =
        DependencyProperty.Register(nameof(GenieProgress), typeof(double), typeof(TransitionHost),
            new PropertyMetadata(0.0, (d, e) => ((TransitionHost)d).ApplyGenieFrame((double)e.NewValue)));

    public double GenieProgress
    {
        get => (double)GetValue(GenieProgressProperty);
        set => SetValue(GenieProgressProperty, value);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _presenter = GetTemplateChild("PART_Content") as ContentPresenter;
        _outgoing = GetTemplateChild("PART_Outgoing") as Image;
        _genieLayer = GetTemplateChild("PART_Genie") as Canvas;

        if (_presenter is not null)
        {
            _incomingScale = new ScaleTransform();
            _incomingSlide = new TranslateTransform();
            _presenter.RenderTransform = Group(_incomingScale, _incomingSlide);
            _presenter.RenderTransformOrigin = new Point(0.5, 0.5);
        }
        if (_outgoing is not null)
        {
            _outgoingScale = new ScaleTransform();
            _outgoingSlide = new TranslateTransform();
            _outgoing.RenderTransform = Group(_outgoingScale, _outgoingSlide);
            _outgoing.RenderTransformOrigin = new Point(0.5, 0.5);
        }
    }

    protected override void OnContentChanged(object oldContent, object newContent)
    {
        base.OnContentChanged(oldContent, newContent);

        if (newContent is null)
            return;

        // oldContent null is the first page shown — nothing to transition from.
        if (oldContent is null || !MotionSettings.Current.AnimationsEnabled || _presenter is null
            || _outgoing is null || _incomingScale is null || _incomingSlide is null
            || _outgoingScale is null || _outgoingSlide is null
            || _presenter.ActualWidth <= 0 || _presenter.ActualHeight <= 0)
        {
            ResetIncoming();
            return;
        }

        if (Genie != GenieMode.None && _genieLayer is not null)
        {
            StartGenie(Genie);
            return;
        }

        _outgoing.Source = Snapshot(_presenter);
        _outgoing.Visibility = Visibility.Visible;

        double direction = SlideFrom >= 0 ? -1 : 1;
        double width = ActualWidth;
        int generation = ++_generation;

        // Rasterize each sheet once and let the GPU transform the cache, instead of re-rendering the
        // live page every frame.
        _outgoing.CacheMode = new BitmapCache();
        _presenter.CacheMode = new BitmapCache();

        void Finish(object? _, EventArgs __)
        {
            if (_generation != generation)
                return;
            _outgoing.Visibility = Visibility.Hidden;
            _outgoing.Source = null;
            _outgoing.CacheMode = null;
            _presenter.CacheMode = null;
        }

        if (ZoomSlide)
        {
            // Outgoing: zoom out in place, then slide off toward the new tab (staying zoomed out).
            _outgoingScale.BeginAnimation(ScaleTransform.ScaleXProperty, KeyFrames((1, 0), (ZoomOut, ZoomEnd), (ZoomOut, 1)));
            _outgoingScale.BeginAnimation(ScaleTransform.ScaleYProperty, KeyFrames((1, 0), (ZoomOut, ZoomEnd), (ZoomOut, 1)));
            _outgoingSlide.BeginAnimation(TranslateTransform.XProperty, KeyFrames((0, 0), (0, ZoomEnd), (direction * width, SlideEnd), (direction * width, 1)));

            // Incoming: wait off the far side (zoomed out) through the zoom-out, slide in, then zoom in.
            _incomingScale.BeginAnimation(ScaleTransform.ScaleXProperty, KeyFrames((ZoomOut, 0), (ZoomOut, SlideEnd), (1, 1)));
            _incomingScale.BeginAnimation(ScaleTransform.ScaleYProperty, KeyFrames((ZoomOut, 0), (ZoomOut, SlideEnd), (1, 1)));
            var slideIn = KeyFrames((-direction * width, 0), (-direction * width, ZoomEnd), (0, SlideEnd), (0, 1));
            slideIn.Completed += Finish;
            _incomingSlide.BeginAnimation(TranslateTransform.XProperty, slideIn);
        }
        else
        {
            // Plain slide (in-page navigation): both pages cross at full size, no zoom.
            ClearScales();
            _outgoingSlide.BeginAnimation(TranslateTransform.XProperty, Slide(0, direction * width));
            var slideIn = Slide(-direction * width, 0);
            slideIn.Completed += Finish;
            _incomingSlide.BeginAnimation(TranslateTransform.XProperty, slideIn);
        }
    }

    private DoubleAnimation Slide(double from, double to) =>
        new(from, to, SlideDuration) { EasingFunction = MotionCurves.Travel };

    private void ClearScales()
    {
        foreach (var scale in new[] { _outgoingScale, _incomingScale })
        {
            if (scale is null)
                continue;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            scale.ScaleX = scale.ScaleY = 1;
        }
    }

    private void StartGenie(GenieMode mode)
    {
        int generation = ++_generation;
        ResetIncoming();

        if (mode == GenieMode.Closing)
        {
            // The presenter still shows the outgoing page; clone it and suck it in over the destination.
            if (BuildStrips(Snapshot(_presenter!)))
                RunGenie(0, 1, GenieClose, generation);
        }
        else
        {
            // Opening: let the presenter adopt the new page, capture it, then pour it out of the gear.
            _presenter!.Opacity = 0;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                if (generation != _generation || _presenter is null)
                    return;
                _presenter.Opacity = 1;
                var shot = Snapshot(_presenter);
                _presenter.Opacity = 0;
                if (BuildStrips(shot))
                    RunGenie(1, 0, GenieOpen, generation, revealPresenter: true);
                else
                    _presenter.Opacity = 1;
            });
        }
    }

    private void RunGenie(double from, double to, TimeSpan duration, int generation, bool revealPresenter = false)
    {
        ApplyGenieFrame(from);
        var animation = new DoubleAnimation(from, to, duration);   // linear; the curve lives in ApplyGenieFrame
        animation.Completed += (_, _) =>
        {
            if (_generation != generation)
                return;
            _genieLayer?.Children.Clear();
            _strips.Clear();
            if (revealPresenter && _presenter is not null)
                _presenter.Opacity = 1;
        };
        BeginAnimation(GenieProgressProperty, animation);
    }

    private bool BuildStrips(ImageSource snapshot)
    {
        if (_genieLayer is null)
            return false;
        _genieLayer.Children.Clear();
        _strips.Clear();

        _genieWidth = ActualWidth;
        _genieHeight = ActualHeight;
        if (_genieWidth <= 0 || _genieHeight <= 0)
            return false;

        // Both directions funnel to/from the gear button; the app supplies its exact centre.
        var target = GenieTarget;
        if (!double.IsNaN(target.X) && !double.IsNaN(target.Y))
        {
            _genieTargetX = target.X;
            _genieTargetY = target.Y;
        }
        else
        {
            _genieTargetX = _genieWidth - 160;   // fallback near the top-right gear
            _genieTargetY = -GenieAboveContent;
        }

        int count = (int)Math.Round(Math.Clamp(_genieWidth / GenieStripPx, 16, GenieMaxStrips));
        double stripW = _genieWidth / count;

        for (int i = 0; i < count; i++)
        {
            double left = i * stripW;
            var scale = new ScaleTransform(1, 1, 0, 0);
            var translate = new TranslateTransform();
            var fill = new ImageBrush(snapshot)
            {
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(left, 0, stripW + 0.75, _genieHeight),
            };
            fill.Freeze();
            var strip = new Rectangle
            {
                Width = stripW + 0.75,   // slight overdraw hides sub-pixel seams between strips
                Height = _genieHeight,
                Fill = fill,
                RenderTransform = Group(scale, translate),
            };
            RenderOptions.SetBitmapScalingMode(strip, BitmapScalingMode.LowQuality);
            Canvas.SetLeft(strip, left);
            _genieLayer.Children.Add(strip);
            _strips.Add(new Strip { Left = left, Width = stripW, Scale = scale, Translate = translate, Element = strip });
        }

        _genieLayer.Visibility = Visibility.Visible;
        return true;
    }

    // p: 0 fully open, 1 fully sucked in. Strips converge on the target point (trigger-side strips
    // leading, for the taffy neck) and squeeze toward it — tracing a funnel through the page.
    private void ApplyGenieFrame(double p)
    {
        if (_strips.Count == 0)
            return;

        bool triggerRight = _genieTargetX > _genieWidth / 2;
        double bend = EaseInOutCubic(Clamp01(p / GenieBendPhase));
        foreach (var strip in _strips)
        {
            double center = strip.Left + strip.Width / 2;
            double u = triggerRight ? (_genieWidth - center) / _genieWidth : center / _genieWidth;  // 0 = trigger side
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

    private static double Clamp01(double v) => Math.Clamp(v, 0, 1);
    private static double Sq(double v) => v * v;
    private static double EaseInOutCubic(double t) => t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;

    // Builds a keyframed animation; fraction is the position on the timeline (0..1) for each value.
    private DoubleAnimationUsingKeyFrames KeyFrames(params (double Value, double Fraction)[] frames)
    {
        var animation = new DoubleAnimationUsingKeyFrames { Duration = Duration };
        foreach (var (value, fraction) in frames)
            animation.KeyFrames.Add(
                new EasingDoubleKeyFrame(value, KeyTime.FromTimeSpan(Duration * fraction), MotionCurves.Travel));
        return animation;
    }

    private static TransformGroup Group(Transform scale, Transform translate)
    {
        var group = new TransformGroup();
        group.Children.Add(scale);
        group.Children.Add(translate);
        return group;
    }

    private void ResetIncoming()
    {
        if (_incomingScale is not null)
        {
            _incomingScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            _incomingScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            _incomingScale.ScaleX = _incomingScale.ScaleY = 1;
        }
        if (_incomingSlide is not null)
        {
            _incomingSlide.BeginAnimation(TranslateTransform.XProperty, null);
            _incomingSlide.X = 0;
        }
        if (_outgoing is not null)
        {
            _outgoing.Visibility = Visibility.Hidden;
            _outgoing.Source = null;
            _outgoing.CacheMode = null;
        }
        if (_presenter is not null)
            _presenter.CacheMode = null;
    }

    private RenderTargetBitmap Snapshot(ContentPresenter presenter)
    {
        double w = presenter.ActualWidth, h = presenter.ActualHeight;
        var dpi = VisualTreeHelper.GetDpi(this);
        double scale = Math.Min(dpi.DpiScaleX, SnapshotMaxScale);
        var bitmap = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(w * scale)), Math.Max(1, (int)Math.Ceiling(h * scale)),
            96 * scale, 96 * scale, PixelFormats.Pbgra32);

        // The page's own surface is transparent over the window background, so composite the content
        // over an opaque fill — otherwise the outgoing sheet is see-through. Pin the brush to the
        // presenter's absolute layout rect (not its content bounds) so short content is not stretched.
        var page = new Rect(0, 0, w, h);
        var content = new VisualBrush(presenter) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = page };
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            if (TryFindResource("BgBrush") is Brush background)
                context.DrawRectangle(background, null, page);
            context.DrawRectangle(content, null, page);
        }
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
}
