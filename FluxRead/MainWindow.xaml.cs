using System;
using System.Windows;
using Flux.Ui.Controls;
using Flux.Ui.Views;
using FluxRead.Views;

namespace FluxRead;

/// <summary>
/// Shell window. Switches between live optical-capture, folder-decode (the codec quality gate),
/// and settings.
/// </summary>
public partial class MainWindow : Window
{
    private readonly FolderDecodeView _folderView;
    private readonly LiveCaptureView _liveView;
    private readonly SettingsView _settingsView;
    private int _currentTab;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class.
    /// </summary>
    /// <param name="folderView">Folder-decode screen.</param>
    /// <param name="liveView">Live optical-capture screen.</param>
    /// <param name="settingsView">Settings screen.</param>
    public MainWindow(FolderDecodeView folderView, LiveCaptureView liveView, SettingsView settingsView)
    {
        _folderView = folderView;
        _liveView = liveView;
        _settingsView = settingsView;
        InitializeComponent();
        WindowChromeAnimator.Attach(this, RootContent);
        ModeHost.Content = _liveView;
    }

    /// <inheritdoc/>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        NativeChrome.EnableWindowAnimations(this);
        Win11Corners.Apply(this);
    }

    private void OnModeChanged(object sender, RoutedEventArgs e)
    {
        if (ModeHost is null)
            return;

        int tab = LiveModeButton.IsChecked == true ? 0
            : FolderModeButton.IsChecked == true ? 1
            : 2;

        // Slide by direction of travel: forward from the right, back from the left.
        ModeHost.SlideFrom = tab >= _currentTab ? 36 : -36;
        _currentTab = tab;
        ModeHost.Content = tab switch { 0 => _liveView, 1 => _folderView, _ => (object)_settingsView };
    }
}
