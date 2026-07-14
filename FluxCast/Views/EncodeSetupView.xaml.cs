using System.Windows;
using System.Windows.Controls;
using FluxCast.Services;
using FluxCast.ViewModels;

namespace FluxCast.Views;

/// <summary>Setup screen view.</summary>
public partial class EncodeSetupView : UserControl
{
    public EncodeSetupView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    // Fit the grid to this window's own monitor once its handle exists (multi-monitor correct).
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is EncodeSetupViewModel vm)
        {
            var (width, height) = DisplayMetrics.PresenterCanvasPixels(Window.GetWindow(this));
            vm.SetDisplayCanvas(width, height);
        }
    }

    private void OnSourceDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnSourceDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } paths
            && DataContext is EncodeSetupViewModel vm)
        {
            await vm.SelectDroppedAsync(paths[0]);
        }
    }
}
