using System.ComponentModel;
using System.Windows;
using Flux.Ui.Controls;
using Flux.Ui.Views;
using FluxRead.ViewModels;
using FluxRead.Views;

namespace FluxRead;

/// <summary>
/// Shell window. Switches between live optical-capture and folder-decode (the codec quality gate);
/// Settings opens as a title-bar page over the same host.
/// </summary>
public partial class MainWindow : Window
{
    private readonly FolderDecodeView _folderView;
    private readonly LiveCaptureView _liveView;
    private readonly SettingsView _settingsView;
    private readonly ShellViewModel _shell;
    private int _currentTab;

    public MainWindow(FolderDecodeView folderView, LiveCaptureView liveView, SettingsView settingsView, ShellViewModel shell)
    {
        _folderView = folderView;
        _liveView = liveView;
        _settingsView = settingsView;
        _shell = shell;
        DataContext = shell;
        InitializeComponent();
        FluxWindowChrome.Attach(this, RootContent);
        ModeHost.Content = _liveView;
        shell.PropertyChanged += OnShellPropertyChanged;
    }

    private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.IsSettingsOpen))
            UpdateContent();
    }

    private void OnModeChanged(object sender, RoutedEventArgs e)
    {
        if (ModeHost is null)
            return;

        int tab = LiveModeButton.IsChecked == true ? 0 : 1;

        // Slide by direction of travel: forward from the right, back from the left.
        ModeHost.SlideFrom = tab >= _currentTab ? 36 : -36;
        _currentTab = tab;
        if (!_shell.IsSettingsOpen)
            ModeHost.Content = TabContent(tab);
    }

    private void UpdateContent()
    {
        if (_shell.IsSettingsOpen)
        {
            ModeHost.SlideFrom = 36;
            ModeHost.Content = _settingsView;
            TabStrip.Visibility = Visibility.Collapsed;
        }
        else
        {
            ModeHost.SlideFrom = -36;
            ModeHost.Content = TabContent(_currentTab);
            TabStrip.Visibility = Visibility.Visible;
        }
    }

    private object TabContent(int tab) => tab == 1 ? _folderView : _liveView;
}
