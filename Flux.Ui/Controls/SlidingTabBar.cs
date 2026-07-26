using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Flux.Ui.Controls;

/// <summary>
/// A horizontal strip that lays out its RadioButton tabs left to right with a single accent "pill"
/// that slides and resizes to sit behind whichever tab is checked, instead of each tab painting its
/// own background. The tabs stay real RadioButtons (so automation/keyboarding is unchanged); the pill
/// is a non-interactive background child. Snaps instantly when <see cref="MotionSettings"/> is off.
/// </summary>
public class SlidingTabBar : Panel
{
    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(320);
    private readonly Border _pill;
    private readonly TranslateTransform _offset = new();
    private bool _ready;

    public SlidingTabBar()
    {
        _pill = new Border
        {
            CornerRadius = new CornerRadius(9),
            HorizontalAlignment = HorizontalAlignment.Left,
            IsHitTestVisible = false,
            RenderTransform = _offset,
            Visibility = Visibility.Hidden,
        };
        _pill.SetResourceReference(Border.BackgroundProperty, "AccentGradientHorizontal");
        Children.Add(_pill);

        AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler(OnTabChecked));
        Loaded += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            MovePill(FindCheckedTab(), animate: false);
            _ready = true;
        });
        SizeChanged += (_, _) => MovePill(FindCheckedTab(), animate: false);
    }

    /// <summary>When true, tabs share the width equally (segmented-control look) rather than sizing to content.</summary>
    public static readonly DependencyProperty StretchProperty = DependencyProperty.Register(
        nameof(Stretch), typeof(bool), typeof(SlidingTabBar),
        new FrameworkPropertyMetadata(false,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    public bool Stretch
    {
        get => (bool)GetValue(StretchProperty);
        set => SetValue(StretchProperty, value);
    }

    /// <summary>Corner radius of the sliding pill (match it to the framing container's inner radius).</summary>
    public static readonly DependencyProperty PillCornerRadiusProperty = DependencyProperty.Register(
        nameof(PillCornerRadius), typeof(double), typeof(SlidingTabBar),
        new PropertyMetadata(9.0, (d, e) => ((SlidingTabBar)d)._pill.CornerRadius = new CornerRadius((double)e.NewValue)));

    public double PillCornerRadius
    {
        get => (double)GetValue(PillCornerRadiusProperty);
        set => SetValue(PillCornerRadiusProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double sumWidth = 0, maxWidth = 0, height = 0;
        int count = 0;
        foreach (UIElement child in InternalChildren)
        {
            if (ReferenceEquals(child, _pill))
                continue;
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            sumWidth += child.DesiredSize.Width;
            maxWidth = Math.Max(maxWidth, child.DesiredSize.Width);
            height = Math.Max(height, child.DesiredSize.Height);
            count++;
        }
        _pill.Measure(new Size(double.PositiveInfinity, height));

        if (Stretch)
            return new Size(double.IsInfinity(availableSize.Width) ? maxWidth * count : availableSize.Width, height);
        return new Size(sumWidth, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var tabs = new List<UIElement>();
        foreach (UIElement child in InternalChildren)
            if (!ReferenceEquals(child, _pill))
                tabs.Add(child);

        if (Stretch && tabs.Count > 0)
        {
            double segment = finalSize.Width / tabs.Count;
            for (int i = 0; i < tabs.Count; i++)
                tabs[i].Arrange(new Rect(i * segment, 0, segment, finalSize.Height));
        }
        else
        {
            double x = 0;
            foreach (UIElement tab in tabs)
            {
                double tabWidth = tab.DesiredSize.Width;
                tab.Arrange(new Rect(x, 0, tabWidth, finalSize.Height));
                x += tabWidth;
            }
        }
        _pill.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));
        return finalSize;
    }

    private void OnTabChecked(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is ToggleButton tab)
            MovePill(tab, animate: true);
    }

    private ToggleButton? FindCheckedTab()
    {
        foreach (UIElement child in InternalChildren)
            if (child is ToggleButton { IsChecked: true } tab)
                return tab;
        return null;
    }

    private void MovePill(ToggleButton? tab, bool animate)
    {
        if (tab is null || !IsAncestorOf(tab) || tab.ActualWidth <= 0)
            return;

        double targetX = tab.TransformToAncestor(this).Transform(new Point(0, 0)).X;
        double targetWidth = tab.ActualWidth;
        _pill.Visibility = Visibility.Visible;

        if (!animate || !_ready || !MotionSettings.Current.AnimationsEnabled)
        {
            _offset.BeginAnimation(TranslateTransform.XProperty, null);
            _pill.BeginAnimation(WidthProperty, null);
            _offset.X = targetX;
            _pill.Width = targetWidth;
            return;
        }

        double fromWidth = double.IsNaN(_pill.Width) ? _pill.ActualWidth : _pill.Width;
        var ease = MotionCurves.Settle;
        _offset.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(_offset.X, targetX, Duration) { EasingFunction = ease });
        _pill.BeginAnimation(WidthProperty,
            new DoubleAnimation(fromWidth, targetWidth, Duration) { EasingFunction = ease });
    }
}
