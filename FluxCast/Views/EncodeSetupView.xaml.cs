using System.Windows;
using System.Windows.Controls;
using FluxCast.Services;
using FluxCast.ViewModels;

namespace FluxCast.Views;

/// <summary>Setup screen view.</summary>
public partial class EncodeSetupView : UserControl
{
    private Window? _window;

    public EncodeSetupView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    // Fit the grid to this window's own monitor once its handle exists (multi-monitor correct), and
    // refit if the window moves to a monitor at a different DPI so the tile size stays honest.
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _window = Window.GetWindow(this);
        if (_window is not null)
            _window.DpiChanged += OnWindowDpiChanged;
        RefitCanvas();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_window is not null)
            _window.DpiChanged -= OnWindowDpiChanged;
        _window = null;
    }

    private void OnWindowDpiChanged(object sender, DpiChangedEventArgs e) => RefitCanvas();

    private void RefitCanvas()
    {
        if (DataContext is EncodeSetupViewModel vm)
        {
            var (width, height) = DisplayMetrics.PresenterCanvasPixels(_window);
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
