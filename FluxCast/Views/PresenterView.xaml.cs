using FluxCast.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.System;

namespace FluxCast.Views;

/// <summary>Presenter view. The frame is laid out at its own device pixels rather than scaled to fit:
/// WinUI has no nearest-neighbour resampling, and resampling loses the tile edges the decoder reads.
/// <c>pixels / RasterizationScale</c> lands on whole pixels, layout rounding being device-pixel aware.</summary>
public sealed partial class PresenterView : UserControl
{
    public PresenterViewModel Vm { get; }

    public PresenterView(PresenterViewModel viewModel)
    {
        Vm = viewModel;
        InitializeComponent();

        FrameArea.SizeChanged += (_, _) => ApplyNativeSize();
        // Frames are cached bitmaps, so revisiting one raises no ImageOpened and the box would keep the
        // previous frame's dimensions — frame 0 is not the payload's shape, so the two disagree.
        FrameImage.RegisterPropertyChangedCallback(Image.SourceProperty, (_, _) => OnFrameSourceChanged());
        KeyDown += OnKeyDown;
    }

    /// <summary>Gets the "of N" label beside the frame box.</summary>
    public string TotalLabel => $"of {Vm.TotalFrames}";

    // The frame last measured and shown, kept so it can stand in while the next one decodes.
    private ImageSource? _shown;

    private void OnFrameSourceChanged()
    {
        if (FrameImage.Source is BitmapImage ready && ready.PixelWidth > 0)
        {
            ApplyNativeSize();   // already decoded, so it draws this pass and nothing is missing
            return;
        }

        // Decoding is asynchronous and an Image draws nothing until it finishes, so the outgoing frame
        // stays underneath at the size it was shown until ImageOpened brings the new one in.
        if (_shown is null)
            return;

        FrameHold.Source = _shown;
        FrameHold.Width = FrameImage.Width;
        FrameHold.Height = FrameImage.Height;
        FrameHold.Visibility = Visibility.Visible;
        FrameImage.Opacity = 0;
    }

    private void OnFrameOpened(object sender, RoutedEventArgs e)
    {
        ApplyNativeSize();
        FrameImage.Opacity = 1;
        FrameHold.Visibility = Visibility.Collapsed;
        FrameHold.Source = null;
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Right && Vm.NextCommand.CanExecute(null))
            Vm.NextCommand.Execute(null);
        else if (e.Key == VirtualKey.Left && Vm.BackCommand.CanExecute(null))
            Vm.BackCommand.Execute(null);
        else
            return;

        e.Handled = true;
    }

    // The flyout's content is not built until it first opens, so the detail is held and applied there.
    private string _sizeWarningDetail = "";

    private void OnSizeWarningOpening(object sender, object e)
    {
        if (SizeWarningDetail is not null)
            SizeWarningDetail.Text = _sizeWarningDetail;
    }

    private void OnDismissSizeWarning(object sender, RoutedEventArgs e) => SizeWarningButton.Flyout?.Hide();

    private void OnGotoKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
            return;

        Vm.GotoCommand.Execute(null);
        e.Handled = true;
    }

    private void ApplyNativeSize()
    {
        // Still decoding, so its shape is unknown. Leave the box exactly as it is and wait for
        // ImageOpened: clearing it lets the frame fall to its natural size for a pass first, which is
        // a blink on every step. Consecutive frames almost always share a shape, so the old box fits.
        if (FrameImage.Source is not BitmapImage frame || frame.PixelWidth == 0)
            return;

        double raster = XamlRoot?.RasterizationScale ?? 1;
        double availableWidth = FrameArea.ActualWidth, availableHeight = FrameArea.ActualHeight;
        if (availableWidth <= 0 || availableHeight <= 0)
            return;

        // The frame takes the space it is given, but only in whole device pixels per tile. Tiles are
        // flat colour, so resampling only ever touches the boundaries between them, and a whole-pixel
        // tile keeps every boundary on an exact device pixel — the edges the decoder reads stay sharp.
        // One factor drives both axes, so the aspect ratio is untouched.
        int tile = Math.Max(1, Vm.TilePixelSize);
        double room = Math.Min(availableWidth * raster / frame.PixelWidth,
                               availableHeight * raster / frame.PixelHeight);
        int tileDevicePixels = Math.Max(1, (int)Math.Floor(tile * room));
        double scale = (double)tileDevicePixels / tile;

        FrameImage.Width = frame.PixelWidth * scale / raster;
        FrameImage.Height = frame.PixelHeight * scale / raster;
        _shown = frame;

        // Below native the frame loses detail the receiver needs, so that is worth saying; above it
        // the frame only gains pixels.
        bool belowNative = tileDevicePixels < tile;
        SizeWarning.Visibility = belowNative ? Visibility.Visible : Visibility.Collapsed;
        if (belowNative)
        {
            // Everything in device pixels, and both axes, since either can be the one holding it back.
            int haveWidth = (int)(availableWidth * raster), haveHeight = (int)(availableHeight * raster);
            int needWidth = Math.Max(0, frame.PixelWidth - haveWidth);
            int needHeight = Math.Max(0, frame.PixelHeight - haveHeight);
            string more = (needWidth, needHeight) switch
            {
                (0, 0) => "A little more room would show it at full size.",
                (> 0, 0) => $"About {needWidth} px more width would show it at full size.",
                (0, > 0) => $"About {needHeight} px more height would show it at full size.",
                _ => $"About {needWidth} px more width and {needHeight} px more height would show it at full size.",
            };
            _sizeWarningDetail =
                $"Each tile is {tileDevicePixels} px wide instead of {tile}.\n" +
                $"Frame is {frame.PixelWidth} × {frame.PixelHeight} px; this window can show " +
                $"{haveWidth} × {haveHeight} px.\n" + more;
        }
    }
}
