using System.Windows;
using FluxRead.Views;

namespace FluxRead;

/// <summary>
/// Shell window. Switches between folder-decode mode (ships first, the codec quality gate) and
/// live optical-capture mode.
/// </summary>
public partial class MainWindow : Window
{
    private readonly FolderDecodeView _folderView;
    private readonly LiveCaptureView _liveView;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class.
    /// </summary>
    /// <param name="folderView">Folder-decode screen.</param>
    /// <param name="liveView">Live optical-capture screen.</param>
    public MainWindow(FolderDecodeView folderView, LiveCaptureView liveView)
    {
        _folderView = folderView;
        _liveView = liveView;
        InitializeComponent();
        ModeHost.Content = _folderView;
    }

    private void OnModeChanged(object sender, RoutedEventArgs e)
    {
        if (ModeHost is null)
            return;

        ModeHost.Content = LiveModeButton.IsChecked == true ? _liveView : _folderView;
    }
}
