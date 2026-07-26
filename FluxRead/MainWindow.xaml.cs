using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using Flux.Ui.Controls;
using Flux.Ui.Services;
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
    private readonly ReceivedItemsView _receivedView;
    private readonly ReceivedItemsViewModel _receivedVm;
    private readonly FolderDecodeViewModel _folderVm;
    private readonly DialogService _dialogs;
    private readonly SettingsView _settingsView;
    private readonly ShellViewModel _shell;
    private int _currentTab;
    private bool _revertingTab;

    public MainWindow(
        FolderDecodeView folderView,
        LiveCaptureView liveView,
        ReceivedItemsView receivedView,
        ReceivedItemsViewModel receivedVm,
        FolderDecodeViewModel folderVm,
        DialogService dialogs,
        SettingsView settingsView,
        ShellViewModel shell)
    {
        _folderView = folderView;
        _liveView = liveView;
        _receivedView = receivedView;
        _receivedVm = receivedVm;
        _folderVm = folderVm;
        _dialogs = dialogs;
        _settingsView = settingsView;
        _shell = shell;
        _receivedVm.ResumeRequested = () => LiveModeButton.IsChecked = true;
        DataContext = shell;
        InitializeComponent();
        FluxWindowChrome.Attach(this, RootContent);
        ModeHost.Content = _liveView;
        shell.PropertyChanged += OnShellPropertyChanged;
        Loaded += (_, _) => UpdateGenieTarget();
        SizeChanged += (_, _) => UpdateGenieTarget();
    }

    private void UpdateGenieTarget()
    {
        if (IsLoaded)
            ModeHost.GenieTarget = TitleBarCtl.GetSettingsAnchor(ModeHost);
    }

    private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.IsSettingsOpen))
            UpdateContent();
    }

    private void OnModeChanged(object sender, RoutedEventArgs e)
    {
        if (ModeHost is null || _revertingTab)
            return;

        int tab = LiveModeButton.IsChecked == true ? 0 : FolderModeButton.IsChecked == true ? 1 : 2;

        if (_currentTab == 1 && tab != 1 && !ConfirmLeavingDecode())
        {
            // Re-check after this event finishes: setting IsChecked inside a sibling's Checked handler
            // races the group's own bookkeeping and can leave every tab unchecked.
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
            {
                _revertingTab = true;
                FolderModeButton.IsChecked = true;
                _revertingTab = false;
            });
            return;
        }

        // Slide by direction of travel: forward from the right, back from the left.
        ModeHost.SlideFrom = tab >= _currentTab ? 36 : -36;
        ModeHost.ZoomSlide = true;   // mode switches are this window's tabs
        ModeHost.Genie = GenieMode.None;
        _currentTab = tab;
        if (tab == 2)
            _receivedVm.Refresh();
        if (!_shell.IsSettingsOpen)
            ModeHost.Content = TabContent(tab);
    }

    // Leaving the tab hides the only view of a running decode, so make the user choose: the decode
    // is cancelled, or the switch is.
    private bool ConfirmLeavingDecode()
    {
        if (!_folderVm.IsDecoding)
            return true;

        // Hold the decode while the prompt is up, so it can't run to completion behind the dialog.
        bool wasPaused = _folderVm.IsPaused;
        _folderVm.SetPaused(true);

        bool cancel = _dialogs.Confirm(
            "Decode in progress",
            "Leaving this tab will stop decoding the frames folder. Cancel the decode?",
            destructive: true);

        if (cancel)
            _folderVm.CancelDecode();
        else if (!wasPaused)
            _folderVm.SetPaused(false);

        return cancel;
    }

    private void UpdateContent()
    {
        if (_shell.IsSettingsOpen)
        {
            ModeHost.Genie = GenieMode.Opening;
            ModeHost.Content = _settingsView;
            TabStrip.Visibility = Visibility.Collapsed;
        }
        else
        {
            ModeHost.Genie = GenieMode.Closing;
            ModeHost.Content = TabContent(_currentTab);
            TabStrip.Visibility = Visibility.Visible;
        }
    }

    private object TabContent(int tab) => tab switch
    {
        1 => _folderView,
        2 => _receivedView,
        _ => _liveView,
    };
}
