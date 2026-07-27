using Flux.Ui.Services;
using Flux.Ui.WinUI.ViewModels;
using Flux.Ui.WinUI.Views;
using Flux.Ui.WinUI.Services;
using FluxRead.WinUI.Services;
using FluxRead.WinUI.ViewModels;
using FluxRead.WinUI.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace FluxRead.WinUI;

public sealed partial class MainWindow : Window
{
    private readonly IntPtr _hwnd;
    private readonly FolderDecodeViewModel _folderVm;
    private readonly DialogService _dialogs;
    private readonly FolderDecodeView _folderView = new();
    private readonly SettingsView _settingsView;
    private readonly LiveCaptureView _liveView;
    private readonly ReceivedItemsView _receivedView;
    private readonly ReceivedItemsViewModel _receivedVm;
    private bool _revertingTab;
    private int _currentTab = 1;

    public MainWindow()
    {
        _folderVm = App.Services.GetRequiredService<FolderDecodeViewModel>();
        _dialogs = App.Services.GetRequiredService<DialogService>();
        _settingsView = new SettingsView(App.Services.GetRequiredService<SettingsViewModel>());
        InitializeComponent();

        _liveView = new LiveCaptureView(
            this,
            App.Services.GetRequiredService<FluxRead.Services.DecodePipelineService>(),
            _dialogs,
            App.Services.GetRequiredService<FluxCore.Transfer.ReceptionHistoryService>(),
            App.Services.GetRequiredService<SettingsService>(),
            App.Services.GetRequiredService<FluxSettings>());

        _receivedVm = App.Services.GetRequiredService<ReceivedItemsViewModel>();
        _receivedVm.ResumeRequested = () => LiveTab.IsChecked = true;
        _receivedView = new ReceivedItemsView(_receivedVm);

        Title = "FluxRead";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarStrip);
        Ambient.Attach(this);

        _hwnd = WindowNative.GetWindowHandle(this);
        Flux.Ui.WinUI.Interop.TaskbarProgress.Current.Attach(_hwnd);

        // Unpackaged WinUI pickers have no implicit parent window, so each one is bound to our HWND.
        _folderVm.PickFolderAsync = PickFolderAsync;
        _folderVm.PickSaveFileAsync = PickSaveTargetAsync;
        _dialogs.PickFolderAsync = PickFolderAsync;
        _dialogs.PickSaveFileAsync = PickSaveTargetAsync;
        _dialogs.XamlRootSource = () => Content?.XamlRoot;

        ModeHost.Page = _folderView;
    }

    private async void OnTabChanged(object sender, RoutedEventArgs e)
    {
        if (ModeHost is null || _revertingTab)
            return;

        if (!ReferenceEquals(sender, FolderTab) && !await ConfirmLeavingDecodeAsync())
        {
            _revertingTab = true;
            FolderTab.IsChecked = true;
            _revertingTab = false;
            TabBar.SnapToChecked();
            return;
        }

        ShowActiveTab();
    }

    private void ShowActiveTab()
    {
        if (ReceivedTab.IsChecked == true)
            _receivedVm.Refresh();

        int tab = LiveTab.IsChecked == true ? 0 : FolderTab.IsChecked == true ? 1 : 2;
        // Slide by direction of travel: forward from the right, back from the left.
        ModeHost.SlideFrom = tab >= _currentTab ? 36 : -36;
        _currentTab = tab;

        ModeHost.Page = FolderTab.IsChecked == true ? _folderView
            : LiveTab.IsChecked == true ? _liveView
            : _receivedView;
    }

    // Leaving the tab hides the only view of a running decode, so make the user choose: the decode
    // is cancelled, or the switch is.
    private async Task<bool> ConfirmLeavingDecodeAsync()
    {
        if (!_folderVm.IsDecoding)
            return true;

        // Hold the decode while the prompt is up, so it can't run to completion behind the dialog.
        bool wasPaused = _folderVm.IsPaused;
        _folderVm.SetPaused(true);

        bool cancel = await _dialogs.ConfirmAsync(
            "Decode in progress",
            "Leaving this tab will stop decoding the frames folder. Cancel the decode?",
            destructive: true);

        if (cancel)
            _folderVm.CancelDecode();
        else if (!wasPaused)
            _folderVm.SetPaused(false);

        return cancel;
    }

    private async Task<string?> PickFolderAsync(string _)
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, _hwnd);
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    private async Task<string?> PickSaveTargetAsync(string _, string suggestedName)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = suggestedName,
        };
        picker.FileTypeChoices.Add("File", [Path.GetExtension(suggestedName) is { Length: > 0 } ext ? ext : "."]);
        InitializeWithWindow.Initialize(picker, _hwnd);
        var file = await picker.PickSaveFileAsync();
        return file?.Path;
    }

    /// <summary>Applies the saved appearance preference; System defers to Windows.</summary>
    public void ApplyTheme(AppThemeMode mode)
    {
        if (Content is FrameworkElement root)
        {
            root.RequestedTheme = mode switch
            {
                AppThemeMode.Light => ElementTheme.Light,
                AppThemeMode.Dark => ElementTheme.Dark,
                _ => ElementTheme.Default,
            };
        }
    }

    private void OnOpenSettings(object sender, RoutedEventArgs e)
    {
        ModeHost.SlideFrom = 36;
        ModeHost.Page = _settingsView;
        TabStrip.Visibility = Visibility.Collapsed;
        SettingsButton.Visibility = Visibility.Collapsed;
        BackButton.Visibility = Visibility.Visible;
    }

    private void OnCloseSettings(object sender, RoutedEventArgs e)
    {
        TabStrip.Visibility = Visibility.Visible;
        SettingsButton.Visibility = Visibility.Visible;
        BackButton.Visibility = Visibility.Collapsed;
        ModeHost.SlideFrom = -36;
        ShowActiveTab();
    }
}
