using FluxCast.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
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
        FrameImage.RegisterPropertyChangedCallback(Image.SourceProperty, (_, _) => ApplyNativeSize());
        KeyDown += OnKeyDown;
    }

    /// <summary>Gets the "of N" label beside the frame box.</summary>
    public string TotalLabel => $"of {Vm.TotalFrames}";

    private void OnFrameOpened(object sender, RoutedEventArgs e) => ApplyNativeSize();

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

        // Below native the frame loses detail the receiver needs, so that is worth saying; above it
        // the frame only gains pixels.
        SizeWarning.Visibility = tileDevicePixels < tile ? Visibility.Visible : Visibility.Collapsed;
    }
}
