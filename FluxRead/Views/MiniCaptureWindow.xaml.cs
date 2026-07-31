using System.ComponentModel;
using Flux.Ui.Controls;
using FluxRead.Interop;
using FluxRead.ViewModels;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace FluxRead.Views;

/// <summary>
/// Compact always-on-top window shown during a live transfer so a single-screen user can watch
/// FluxCast and the transfer status at once. Collapses to a two-line strip (header + progress) when
/// screen space is tight, keeping its bottom-right corner pinned.
/// </summary>
public sealed partial class MiniCaptureWindow : Window
{
    private const double Width = 400;
    private const double ExpandedHeight = 372;
    private const double CollapsedHeight = 128;
    private const double CornerMargin = 24;
    private static readonly TimeSpan ResizeDuration = TimeSpan.FromMilliseconds(240);
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(16);

    private readonly Action _onPauseToggle;
    private readonly Action _onCancel;
    private readonly Action<bool> _onExpandedChanged;
    private readonly IntPtr _hwnd;
    private readonly DispatcherTimer _resizeTicker = new() { Interval = FrameInterval };
    private DateTime _resizeStart;
    private int _resizeFromHeight;
    private int _resizeToHeight;
    private bool _expanded;

    public LiveCaptureViewModel Vm { get; }

    public MiniCaptureWindow(
        LiveCaptureViewModel viewModel, IntPtr ownerHandle, Action onPauseToggle, Action onCancel,
        bool expanded, Action<bool> onExpandedChanged)
    {
        Vm = viewModel;
        _onPauseToggle = onPauseToggle;
        _onCancel = onCancel;
        _onExpandedChanged = onExpandedChanged;
        _expanded = expanded;
        InitializeComponent();

        Title = "FluxRead — Capturing";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(HeaderBar);

        _hwnd = WindowNative.GetWindowHandle(this);
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }

        _resizeTicker.Tick += OnResizeTick;
        Vm.PropertyChanged += OnViewModelPropertyChanged;
        Closed += (_, _) => Vm.PropertyChanged -= OnViewModelPropertyChanged;
        // Closing the window stops the transfer, which is what the WPF window's own close button did.
        AppWindow.Closing += (_, _) => _onCancel();

        ApplyBodies();
        UpdatePauseIcon();
        WindowPlacement.PlaceBottomRightOfMonitor(
            _hwnd, ownerHandle, Width, _expanded ? ExpandedHeight : CollapsedHeight, CornerMargin);
    }

    /// <summary>Applies the theme the shell is using; a second window does not inherit it.</summary>
    public void ApplyTheme(ElementTheme theme)
    {
        if (Content is FrameworkElement root)
            root.RequestedTheme = theme;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LiveCaptureViewModel.IsPaused))
            UpdatePauseIcon();
    }

    private void UpdatePauseIcon()
    {
        PauseIcon.Visibility = Vm.IsPaused ? Visibility.Collapsed : Visibility.Visible;
        PlayIcon.Visibility = Vm.IsPaused ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnPauseToggle(object sender, RoutedEventArgs e) => _onPauseToggle();

    private void OnCancel(object sender, RoutedEventArgs e) => _onCancel();

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
        ChevronDownIcon.Visibility = _expanded ? Visibility.Visible : Visibility.Collapsed;
        ChevronUpIcon.Visibility = _expanded ? Visibility.Collapsed : Visibility.Visible;
        string label = _expanded ? "Collapse" : "Expand";
        Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(CollapseButton, label);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(CollapseButton, label);
    }

    // The bottom edge stays put, and an AppWindow has no animatable height, so this tweens by hand.
    private void ResizeToState()
    {
        double scale = Content.XamlRoot?.RasterizationScale ?? 1;
        _resizeFromHeight = AppWindow.Size.Height;
        _resizeToHeight = (int)Math.Round((_expanded ? ExpandedHeight : CollapsedHeight) * scale);

        if (!MotionSettings.Current.AnimationsEnabled)
        {
            ApplyHeight(_resizeToHeight);
            return;
        }

        _resizeStart = DateTime.UtcNow;
        _resizeTicker.Start();
    }

    private void OnResizeTick(object? sender, object e)
    {
        double t = (DateTime.UtcNow - _resizeStart) / ResizeDuration;
        if (t >= 1)
        {
            _resizeTicker.Stop();
            ApplyHeight(_resizeToHeight);
            return;
        }

        double eased = 1 - Math.Pow(1 - t, 3);
        ApplyHeight((int)Math.Round(_resizeFromHeight + (_resizeToHeight - _resizeFromHeight) * eased));
    }

    private void ApplyHeight(int height)
    {
        int bottom = AppWindow.Position.Y + AppWindow.Size.Height;
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(
            AppWindow.Position.X, bottom - height, AppWindow.Size.Width, height));
    }
}
