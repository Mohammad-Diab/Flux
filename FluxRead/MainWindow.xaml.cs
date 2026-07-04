using System;
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
        Controls.WindowChromeAnimator.Attach(this, RootContent);
        ModeHost.Content = _liveView;
    }

    /// <inheritdoc/>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Controls.NativeChrome.EnableWindowAnimations(this);
        Controls.Win11Corners.Apply(this);
    }

    private void OnModeChanged(object sender, RoutedEventArgs e)
    {
        if (ModeHost is null)
            return;

        // Directional slide: the left tab (Live) enters from the left, the right tab (Folder)
        // from the right — so switching tabs slides the way you'd expect.
        bool live = LiveModeButton.IsChecked == true;
        ModeHost.SlideFrom = live ? -36 : 36;
        ModeHost.Content = live ? _liveView : _folderView;
    }
}
