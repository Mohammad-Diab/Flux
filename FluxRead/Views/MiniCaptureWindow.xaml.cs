using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Flux.Ui.Controls;
using FluxRead.Interop;
using FluxRead.ViewModels;

namespace FluxRead.Views;

/// <summary>
/// Compact always-on-top window shown during a live transfer so a single-screen user can watch
/// FluxCast and the transfer status at once. Collapses to a two-line strip (header + progress) when
/// screen space is tight, keeping its bottom-right corner pinned.
/// </summary>
public partial class MiniCaptureWindow : Window
{
    private const double ExpandedHeight = 372;
    private const double CollapsedHeight = 128;
    private const double CornerMargin = 24;
    private static readonly Geometry ChevronUp = Geometry.Parse("M1,7 L6,2 L11,7");
    private static readonly Geometry ChevronDown = Geometry.Parse("M1,3 L6,8 L11,3");
    private static readonly Geometry PauseBars = Geometry.Parse("M1,1 L4,1 L4,11 L1,11 Z M8,1 L11,1 L11,11 L8,11 Z");
    private static readonly Geometry PlayTriangle = Geometry.Parse("M2,1 L11,6 L2,11 Z");
    private static readonly TimeSpan ResizeDuration = TimeSpan.FromMilliseconds(240);

    private readonly LiveCaptureViewModel _vm;
    private readonly Action _onPauseToggle;
    private readonly Action _onCancel;
    private readonly Action<bool> _onExpandedChanged;
    private bool _expanded;

    public MiniCaptureWindow(
        LiveCaptureViewModel viewModel, Action onPauseToggle, Action onCancel,
        bool expanded, Action<bool> onExpandedChanged)
    {
        _vm = viewModel;
        _onPauseToggle = onPauseToggle;
        _onCancel = onCancel;
        _onExpandedChanged = onExpandedChanged;
        _expanded = expanded;
        DataContext = viewModel;
        InitializeComponent();
        Flux.Ui.Controls.FluxWindowChrome.AttachCompact(this);

        _vm.PropertyChanged += OnViewModelPropertyChanged;
        Loaded += (_, _) =>
        {
            ApplyBodies();
            UpdatePauseIcon();
            Height = _expanded ? ExpandedHeight : CollapsedHeight;
            ParkInCorner();
        };
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LiveCaptureViewModel.IsPaused))
            UpdatePauseIcon();
    }

    private void UpdatePauseIcon() => PauseIcon.Data = _vm.IsPaused ? PlayTriangle : PauseBars;

    private void OnPauseToggle(object sender, RoutedEventArgs e) => _onPauseToggle();

    private void OnCancel(object sender, RoutedEventArgs e) => _onCancel();

    private void OnClose(object sender, RoutedEventArgs e) => _onCancel();

    private void OnToggleSize(object sender, RoutedEventArgs e)
    {
        _expanded = !_expanded;
        ApplyBodies();
        ResizeToState();
        _onExpandedChanged(_expanded);
    }

    private void ApplyBodies()
    {
        ExpandedBody.Visibility = _expanded ? Visibility.Visible : Visibility.Collapsed;
        CollapsedBody.Visibility = _expanded ? Visibility.Collapsed : Visibility.Visible;
        CollapseIcon.Data = _expanded ? ChevronUp : ChevronDown;
        CollapseButton.ToolTip = _expanded ? "Collapse" : "Expand";
    }

    // Grow/shrink from the pinned bottom-right corner: the bottom edge stays put, so Top moves opposite
    // to Height. Snaps instantly when motion (performance mode / the Windows animation setting) is off.
    private void ResizeToState()
    {
        double targetHeight = _expanded ? ExpandedHeight : CollapsedHeight;
        double bottom = Top + ActualHeight;
        double targetTop = bottom - targetHeight;

        BeginAnimation(HeightProperty, null);
        BeginAnimation(TopProperty, null);

        if (!MotionSettings.Current.AnimationsEnabled)
        {
            Height = targetHeight;
            Top = targetTop;
            return;
        }

        var ease = MotionCurves.Travel;
        BeginAnimation(HeightProperty, new DoubleAnimation(ActualHeight, targetHeight, ResizeDuration) { EasingFunction = ease });
        BeginAnimation(TopProperty, new DoubleAnimation(Top, targetTop, ResizeDuration) { EasingFunction = ease });
    }

    // Park bottom-right of the monitor the main window is on, so it lands on the user's screen.
    private void ParkInCorner()
    {
        var self = new WindowInteropHelper(this).Handle;
        var owner = Owner is null ? IntPtr.Zero : new WindowInteropHelper(Owner).Handle;
        WindowPlacement.PlaceBottomRightOfMonitor(self, owner, Width, Height, CornerMargin);
    }
}
