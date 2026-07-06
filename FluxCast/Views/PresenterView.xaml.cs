using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FluxCore.Framing;

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

    private void UpdateSizeWarning()
    {
        double scale = Math.Min(
            FrameArea.ActualWidth / FrameFormat.FrameWidthPx,
            FrameArea.ActualHeight / FrameFormat.FrameHeightPx);

        // Below ~0.6x the 8px tiles fall under ~5px and get fragile once a capture recompresses.
        SizeWarning.Visibility = scale is > 0 and < 0.6 ? Visibility.Visible : Visibility.Collapsed;
    }
}
