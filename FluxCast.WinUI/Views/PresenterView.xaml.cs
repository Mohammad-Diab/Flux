using FluxCast.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.System;

namespace FluxCast.WinUI.Views;

/// <summary>
/// Presenter view. The frame is laid out at exactly its own device pixels rather than scaled to fit:
/// WinUI offers no nearest-neighbour resampling, and a resampled frame loses the tile edges the
/// decoder reads. Sizing to <c>pixels / RasterizationScale</c> lands on whole device pixels, because
/// WinUI's layout rounding is device-pixel aware.
/// </summary>
public sealed partial class PresenterView : UserControl
{
    public PresenterViewModel Vm { get; }

    public PresenterView(PresenterViewModel viewModel)
    {
        Vm = viewModel;
        InitializeComponent();

        FrameArea.SizeChanged += (_, _) => ApplyNativeSize();
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
        if (FrameImage.Source is not BitmapImage frame || frame.PixelWidth == 0)
            return;

        double scale = XamlRoot?.RasterizationScale ?? 1;
        FrameImage.Width = frame.PixelWidth / scale;
        FrameImage.Height = frame.PixelHeight / scale;

        // Nothing can be scaled down without blurring the tiles, so warn instead when it will not fit.
        bool fits = FrameImage.Width <= FrameArea.ActualWidth + 0.5
                    && FrameImage.Height <= FrameArea.ActualHeight + 0.5;
        SizeWarning.Visibility = fits ? Visibility.Collapsed : Visibility.Visible;
    }
}
