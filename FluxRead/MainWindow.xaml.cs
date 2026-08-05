using Flux.Ui;
using Flux.Ui.Controls;
using Flux.Ui.Services;
using Flux.Ui.ViewModels;
using Flux.Ui.Views;
using FluxRead.Services;
using FluxRead.ViewModels;
using FluxRead.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace FluxRead;

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
    private int _currentTab;

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
        VersionText.Text = $"v{AppVersion.Current}";
        MotionIcon.Attach(SettingsButton, SettingsAnimatedIcon);
        MotionIcon.Attach(BackButton, BackAnimatedIcon);
        VersionChip.Visibility = AppVersion.Current.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarStrip);
        Ambient.Attach(this);

        _hwnd = WindowNative.GetWindowHandle(this);
        Flux.Ui.Interop.TaskbarProgress.Current.Attach(_hwnd);

        // Unpackaged WinUI pickers have no implicit parent window, so each one is bound to our HWND.
        _folderVm.PickFolderAsync = PickFolderAsync;
        _folderVm.PickSaveFileAsync = PickSaveTargetAsync;
        _dialogs.PickFolderAsync = PickFolderAsync;
        _dialogs.PickSaveFileAsync = PickSaveTargetAsync;
        _dialogs.XamlRootSource = () => Content?.XamlRoot;

        ModeHost.AmbientSource = Ambient;

        // Measure the genie's funnel point while the gear is still visible, as the WPF shell did.
        // Its distance from the right edge survives both the gear collapsing and window resizes.
        SettingsButton.Loaded += (_, _) => MeasureGear();
        SettingsButton.SizeChanged += (_, _) => MeasureGear();
        if (Content is FrameworkElement root)
        {
            root.SizeChanged += (_, _) =>
            {
                MeasureGear();
                UpdateGenieTarget();
            };
        }

        ModeHost.Page = _liveView;
    }

    private double _gearFromRight = double.NaN, _gearCenterY;

    private void MeasureGear()
    {
        if (Content is not FrameworkElement root || SettingsButton.Visibility == Visibility.Collapsed
            || SettingsButton.ActualWidth <= 0)
            return;

        var center = SettingsButton.TransformToVisual(root).TransformPoint(
            new Windows.Foundation.Point(SettingsButton.ActualWidth / 2, SettingsButton.ActualHeight / 2));
        _gearFromRight = root.ActualWidth - center.X;
        _gearCenterY = center.Y;
        UpdateGenieTarget();
    }

    private void UpdateGenieTarget()
    {
        if (double.IsNaN(_gearFromRight) || Content is not FrameworkElement root || ModeHost is null)
            return;

        ModeHost.GenieTarget = root.TransformToVisual(ModeHost).TransformPoint(
            new Windows.Foundation.Point(root.ActualWidth - _gearFromRight, _gearCenterY));
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

    private void ShowActiveTab(GenieMode genie = GenieMode.None)
    {
        ModeHost.Genie = genie;
        if (ReceivedTab.IsChecked == true)
            _receivedVm.Refresh();

        int tab = LiveTab.IsChecked == true ? 0 : FolderTab.IsChecked == true ? 1 : 2;
        // Slide by direction of travel: forward from the right, back from the left.
        ModeHost.SlideFrom = tab >= _currentTab ? 36 : -36;
        _currentTab = tab;

        // Later tabs leave the first tab as bare text, so the page indents to that text column.
        ModeHost.Margin = new Thickness(tab == 0 ? 0 : 16, 0, 0, 0);

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
        // Settings pours out of the gear, and is sucked back into it on the way out.
        ModeHost.Genie = GenieMode.Opening;
        DeferChromeToGenie(settingsOpen: true);
        ModeHost.Page = _settingsView;
    }

    private void OnCloseSettings(object sender, RoutedEventArgs e)
    {
        ModeHost.SlideFrom = -36;
        DeferChromeToGenie(settingsOpen: false);
        ShowActiveTab(GenieMode.Closing);
    }

    // The genie picks the moment: it freezes the screen, raises GenieAnchored, and the chrome swaps
    // under the warp where the strip row resizing cannot be seen. Settled is the fallback for a warp
    // that never ran (reduced motion, capture failure, resize abort).
    private void DeferChromeToGenie(bool settingsOpen)
    {
        void ApplyChrome()
        {
            TabStrip.Visibility = settingsOpen ? Visibility.Collapsed : Visibility.Visible;
            SettingsButton.Visibility = settingsOpen ? Visibility.Collapsed : Visibility.Visible;
            BackButton.Visibility = settingsOpen ? Visibility.Visible : Visibility.Collapsed;
        }

        bool applied = false;
        void Apply(object? _, EventArgs __)
        {
            applied = true;
            ApplyChrome();
            (Content as FrameworkElement)?.UpdateLayout();
            UpdateGenieTarget();
        }
        void Done(object? _, EventArgs __)
        {
            ModeHost.GenieAnchored -= Apply;
            ModeHost.Settled -= Done;
            if (!applied)
                ApplyChrome();
        }
        ModeHost.GenieAnchored += Apply;
        ModeHost.Settled += Done;
        UpdateGenieTarget();   // closing latches the funnel point before the chrome moves the host
    }
}
