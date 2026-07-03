using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FluxCast.Views;

/// <summary>
/// Presenter view. Sizes the frame image so one bitmap pixel maps to exactly one physical
/// screen pixel at any display scaling — resampled tiles cannot be decoded reliably.
/// </summary>
public partial class PresenterView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PresenterView"/> class.
    /// </summary>
    public PresenterView()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        FrameImage.SizeChanged += (_, _) => UpdateSizeWarning();
        FrameArea.SizeChanged += (_, _) => UpdateSizeWarning();
    }

    private Window? _window;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _window = Window.GetWindow(this);
        if (_window is not null)
            _window.DpiChanged += OnDpiChanged;

        ApplyPixelPerfectSize();
        Keyboard.Focus(this);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_window is not null)
            _window.DpiChanged -= OnDpiChanged;
        _window = null;
    }

    private void OnDpiChanged(object sender, DpiChangedEventArgs e) => ApplyPixelPerfectSize();

    private void ApplyPixelPerfectSize()
    {
        if (FrameImage.Source is not System.Windows.Media.Imaging.BitmapSource bitmap)
            return;

        var dpi = VisualTreeHelper.GetDpi(this);
        FrameImage.Width = bitmap.PixelWidth / dpi.DpiScaleX;
        FrameImage.Height = bitmap.PixelHeight / dpi.DpiScaleY;

        UpdateSizeWarning();
    }

    private void UpdateSizeWarning()
    {
        bool tooSmall = FrameArea.ActualWidth + 0.5 < FrameImage.Width ||
                        FrameArea.ActualHeight + 0.5 < FrameImage.Height;
        SizeWarning.Visibility = tooSmall ? Visibility.Visible : Visibility.Collapsed;
    }
}
