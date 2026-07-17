using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluxCast.ViewModels;

namespace FluxCast.Views;

/// <summary>
/// Presenter view. The frame image scales uniformly (nearest-neighbor, aspect-locked) to fill
/// the available area, so the window is freely resizable; the decoder locates tiles by the
/// corner fiducials and a homography, so any display size works down to a practical minimum.
/// </summary>
public partial class PresenterView : UserControl
{
    public PresenterView()
    {
        InitializeComponent();
        Loaded += (_, _) => Keyboard.Focus(this);
        FrameArea.SizeChanged += (_, _) => UpdateSizeWarning();
    }

    private void OnFrameChanged(object sender, DataTransferEventArgs e) => UpdateSizeWarning();

    private void UpdateSizeWarning()
    {
        if (FrameImage.Source is not BitmapSource frame || DataContext is not PresenterViewModel vm)
        {
            SizeWarning.Visibility = Visibility.Collapsed;
            return;
        }

        // FrameArea is measured in DIPs but the frame's PixelWidth/Height are device pixels, so both
        // sides must be brought into device pixels — otherwise the ratio is off by the DPI scale and
        // the warning fires on high-DPI screens even when the tiles are large enough.
        var dpi = VisualTreeHelper.GetDpi(this);
        double scale = Math.Min(
            FrameArea.ActualWidth * dpi.DpiScaleX / frame.PixelWidth,
            FrameArea.ActualHeight * dpi.DpiScaleY / frame.PixelHeight);

        // Warn once on-screen tiles fall below the size this cast's palette needs to survive a capture.
        double onScreenTilePx = scale * vm.TilePixelSize;
        SizeWarning.Visibility = scale > 0 && onScreenTilePx < vm.SafeTilePx ? Visibility.Visible : Visibility.Collapsed;
    }
}
