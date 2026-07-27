using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;

namespace Flux.Ui.WinUI.Controls;

/// <summary>A strip of RadioButton tabs with one accent pill sliding behind whichever is checked. Snaps
/// instantly when <see cref="MotionSettings"/> is off.</summary>
public class SlidingTabBar : Panel
{
    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(320);

    private readonly Border _pill;
    private readonly TranslateTransform _offset = new();
    private readonly HashSet<ToggleButton> _hooked = [];
    private Storyboard? _running;
    private bool _ready;

    public SlidingTabBar()
    {
        _pill = new Border
        {
            CornerRadius = new CornerRadius(9),
            HorizontalAlignment = HorizontalAlignment.Left,
            IsHitTestVisible = false,
            RenderTransform = _offset,
            Visibility = Visibility.Collapsed,
        };
        if (Application.Current.Resources.TryGetValue("AccentGradientHorizontal", out var brush))
            _pill.Background = (Brush)brush;
        Children.Add(_pill);

        Loaded += (_, _) =>
        {
            HookTabs();
            MovePill(FindCheckedTab(), animate: false);
            _ready = true;
        };
        SizeChanged += (_, _) => MovePill(FindCheckedTab(), animate: false);
    }

    /// <summary>When true, tabs share the width equally (segmented look) rather than sizing to content.</summary>
    public static readonly DependencyProperty StretchProperty = DependencyProperty.Register(
        nameof(Stretch), typeof(bool), typeof(SlidingTabBar),
        new PropertyMetadata(false, (d, _) => ((SlidingTabBar)d).InvalidateMeasure()));

    public bool Stretch
    {
        get => (bool)GetValue(StretchProperty);
        set => SetValue(StretchProperty, value);
    }

    /// <summary>Corner radius of the pill; match it to the framing container's inner radius.</summary>
    public static readonly DependencyProperty PillCornerRadiusProperty = DependencyProperty.Register(
        nameof(PillCornerRadius), typeof(double), typeof(SlidingTabBar),
        new PropertyMetadata(9.0, (d, e) =>
            ((SlidingTabBar)d)._pill.CornerRadius = new CornerRadius((double)e.NewValue)));

    public double PillCornerRadius
    {
        get => (double)GetValue(PillCornerRadiusProperty);
        set => SetValue(PillCornerRadiusProperty, value);
    }

    /// <summary>Puts the pill on the checked tab with no animation — for reverting a rejected tab switch,
    /// which would otherwise slide out and back.</summary>
    public void SnapToChecked()
    {
        _running?.Stop();
        _running = null;
        MovePill(FindCheckedTab(), animate: false);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double sumWidth = 0, maxWidth = 0, height = 0;
        int count = 0;
        foreach (var child in Children)
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
        foreach (var child in Children)
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
            foreach (var tab in tabs)
            {
                double tabWidth = tab.DesiredSize.Width;
                tab.Arrange(new Rect(x, 0, tabWidth, finalSize.Height));
                x += tabWidth;
            }
        }
        _pill.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));
        return finalSize;
    }

    // WinUI exposes no ToggleButton.CheckedEvent to AddHandler with, so each tab is subscribed directly.
    private void HookTabs()
    {
        foreach (var child in Children)
        {
            if (child is ToggleButton tab && _hooked.Add(tab))
                tab.Checked += (_, _) => MovePill(FindCheckedTab(), animate: true);
        }
    }

    private ToggleButton? FindCheckedTab()
    {
        foreach (var child in Children)
            if (child is ToggleButton { IsChecked: true } tab)
                return tab;
        return null;
    }

    private void MovePill(ToggleButton? tab, bool animate)
    {
        if (tab is null || tab.ActualWidth <= 0)
            return;

        double targetX = tab.TransformToVisual(this).TransformPoint(new Point(0, 0)).X;
        double targetWidth = tab.ActualWidth;
        _pill.Visibility = Visibility.Visible;

        if (!animate || !_ready || !MotionSettings.Current.AnimationsEnabled)
        {
            _offset.X = targetX;
            _pill.Width = targetWidth;
            return;
        }

        double fromWidth = double.IsNaN(_pill.Width) ? _pill.ActualWidth : _pill.Width;
        var ease = new QuinticEase { EasingMode = EasingMode.EaseOut };
        var slide = new DoubleAnimation
        {
            From = _offset.X, To = targetX, Duration = Duration, EasingFunction = ease,
        };
        // Width is a layout property, so WinUI silently drops the animation without this flag.
        var grow = new DoubleAnimation
        {
            From = fromWidth, To = targetWidth, Duration = Duration, EasingFunction = ease,
            EnableDependentAnimation = true,
        };

        Storyboard.SetTarget(slide, _offset);
        Storyboard.SetTargetProperty(slide, "X");
        Storyboard.SetTarget(grow, _pill);
        Storyboard.SetTargetProperty(grow, "Width");

        var board = new Storyboard();
        board.Children.Add(slide);
        board.Children.Add(grow);
        _running = board;
        board.Begin();
    }
}
