using System.IO;
using Flux.Ui;
using Flux.Ui.Controls;
using Flux.Ui.Services;
using Flux.Ui.ViewModels;
using Flux.Ui.Views;
using FluxCast.Services;
using FluxCast.ViewModels;
using FluxCast.Views;
using FluxCore.Compression;
using FluxCore.Ecc;
using FluxCore.Framing;
using FluxCore.Imaging;
using FluxCore.Transfer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace FluxCast;

/// <summary>Owns navigation across the Cast and History tabs, the setup/progress/presenter flow, and
/// the Settings page. WinUI has no implicit DataTemplate-by-type, so the shell holds the views.</summary>
public sealed partial class MainWindow : Window
{
    private readonly IntPtr _hwnd;
    private readonly DialogService _dialogs;
    private readonly SettingsView _settingsView;

    private object? _castScreen;
    private RecentCastsView? _historyScreen;
    private bool _isSettingsOpen;
    private int _lastNavIndex;
    private bool _closeConfirmed;

    /// <summary>Gets the root directory for encode sessions.</summary>
    public static string SessionRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Flux", "FluxCast", "sessions");

    // Throwaway location for channel-test frames; kept out of the history session root.
    private static string ChannelTestRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Flux", "FluxCast", "channel-test");

    public MainWindow()
    {
        _dialogs = App.Services.GetRequiredService<DialogService>();
        InitializeComponent();

        Title = "FluxCast";
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
        _dialogs.PickFileAsync = PickFileAsync;
        _dialogs.PickFolderAsync = PickFolderAsync;
        _dialogs.XamlRootSource = () => Content?.XamlRoot;

        _settingsView = new SettingsView(App.Services.GetRequiredService<SettingsViewModel>());
        AppWindow.Closing += OnClosing;

        // Measure the genie's funnel point while the gear is still visible, as the WPF shell did.
        SettingsButton.Loaded += (_, _) => UpdateGenieTarget();
        SettingsButton.SizeChanged += (_, _) => UpdateGenieTarget();
        if (Content is FrameworkElement root)
            root.SizeChanged += (_, _) => UpdateGenieTarget();

        ShowSetup();
    }

    private void UpdateGenieTarget()
    {
        if (SettingsButton.ActualWidth <= 0 || ContentHost is null)
            return;

        ContentHost.GenieTarget = SettingsButton.TransformToVisual(ContentHost).TransformPoint(
            new Windows.Foundation.Point(SettingsButton.ActualWidth / 2, SettingsButton.ActualHeight / 2));
    }

    // A Closing handler cannot await, so an in-progress cast vetoes the first close, then closes
    // itself once the prompt comes back confirmed.
    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_closeConfirmed || ClosePrompt() is not { } prompt)
            return;

        args.Cancel = true;
        _ = ConfirmThenCloseAsync(prompt.Title, prompt.Message);
    }

    private (string Title, string Message)? ClosePrompt() => _castScreen switch
    {
        EncodeProgressView => ("Stop encoding?",
            "Frames are still being generated. Close FluxCast and discard this cast?"),
        PresenterView => ("End cast?", "A cast is in progress. Close FluxCast?"),
        _ => null,
    };

    private async Task ConfirmThenCloseAsync(string title, string message)
    {
        if (!await _dialogs.ConfirmAsync(title, message, destructive: true))
            return;

        _closeConfirmed = true;
        Close();
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

    private void ShowSetup()
    {
        var vm = new EncodeSetupViewModel(
            App.Services.GetRequiredService<SourceValidator>(), _dialogs, StartEncode, ShowTestFrame,
            DisplayMetrics.PresenterCanvasPixels(this));
        _castScreen = new EncodeSetupView(vm);
        UpdateCurrent();
    }

    private void StartEncode(string sourcePath, EncodeOptions options) =>
        StartEncode(sourcePath, SessionRoot, options);

    private void StartEncode(string sourcePath, string root, EncodeOptions options)
    {
        var vm = new EncodeProgressViewModel(
            App.Services.GetRequiredService<FluxEncodeService>(), sourcePath, root, options,
            onCompleted: ShowPresenter,
            onCancelledOrFailed: ShowSetup,
            App.Services.GetRequiredService<ILoggerFactory>().CreateLogger<EncodeProgressViewModel>());
        _castScreen = new EncodeProgressView(vm);
        UpdateCurrent();
    }

    private void ShowPresenter(EncodeSessionResult session)
    {
        _castScreen = new PresenterView(new PresenterViewModel(session, ShowSetup, _dialogs));
        UpdateCurrent();
    }

    /// <summary>
    /// Renders a throwaway 2-frame transfer (frame 0 + one full payload frame at the chosen settings)
    /// and presents it, so the user can capture it in FluxRead to confirm the channel before a real
    /// transfer. Uses a synthetic payload and a temp root, so nothing lands in the cast history.
    /// </summary>
    private void ShowTestFrame(EncodeOptions options)
    {
        var testOptions = options with { Compress = false };
        StartEncode(PrepareTestSource(testOptions), ChannelTestRoot, testOptions);
    }

    // Writes exactly one frame's worth of deterministic random bytes, so the test frame is fully
    // populated (exercises the whole palette) and the transfer is frame 0 + a single payload frame.
    private static string PrepareTestSource(EncodeOptions options)
    {
        try { if (Directory.Exists(ChannelTestRoot)) Directory.Delete(ChannelTestRoot, recursive: true); }
        catch { /* best-effort clean of the previous test */ }
        Directory.CreateDirectory(ChannelTestRoot);

        var layout = new FrameLayout(options.GridWidthTiles, options.GridHeightTiles, options.TilePixelSize);
        int codewords = layout.CodewordsForBits(PaletteGenerator.BitsForCount(options.ColorCount));
        var bytes = new byte[options.EccLevel.PayloadBytesPerFrame(codewords)];
        new Random(20260712).NextBytes(bytes);

        var path = Path.Combine(ChannelTestRoot, "channel-test.bin");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private void OnTabChanged(object sender, RoutedEventArgs e)
    {
        if (ContentHost is null)
            return;

        if (HistoryTab.IsChecked == true)
            ShowHistory();
        else
            UpdateCurrent();
    }

    private void ShowHistory()
    {
        _historyScreen ??= new RecentCastsView(new RecentCastsViewModel(
            App.Services.GetRequiredService<CastHistoryService>(), _dialogs,
            App.Services.GetRequiredService<CompressionService>(), SessionRoot, ResumeCast));
        _historyScreen.Vm.Refresh();
        UpdateCurrent();
    }

    private void ResumeCast(CastHistoryEntry entry)
    {
        var session = App.Services.GetRequiredService<CastHistoryService>()
            .OpenForPresenting(entry.SessionDirectory);
        _castScreen = new PresenterView(new PresenterViewModel(session, ShowSetup, _dialogs));
        _isSettingsOpen = false;
        CastTab.IsChecked = true;
        UpdateCurrent();
    }

    private void OnOpenSettings(object sender, RoutedEventArgs e)
    {
        _isSettingsOpen = true;
        UpdateCurrent();
    }

    private void OnBack(object sender, RoutedEventArgs e)
    {
        if (_isSettingsOpen)
        {
            _isSettingsOpen = false;
            UpdateCurrent();
        }
        else if (_castScreen is PresenterView presenter)
        {
            presenter.Vm.CloseCommand.Execute(null);
        }
    }

    private void UpdateCurrent()
    {
        bool presenting = !_isSettingsOpen && HistoryTab.IsChecked != true && _castScreen is PresenterView;
        bool chromeReturning = !_isSettingsOpen && _lastNavIndex == 2;

        void ApplyChrome()
        {
            // Settings leaves the strip's row occupied but blank. Collapsing it hands that height to the
            // content row, and the page visibly climbs on the way in and drops on the way out. The
            // presenter is the one screen that really wants the space.
            TabStrip.Visibility = presenting ? Visibility.Collapsed : Visibility.Visible;
            TabStrip.Opacity = _isSettingsOpen ? 0 : 1;
            TabStrip.IsHitTestVisible = !_isSettingsOpen;
            SettingsButton.Visibility = _isSettingsOpen || presenting ? Visibility.Collapsed : Visibility.Visible;
            BackButton.Visibility = _isSettingsOpen || presenting ? Visibility.Visible : Visibility.Collapsed;
        }

        // Leaving settings, the tab strip takes its row back and shoves the content down. Doing that up
        // front lands it while the settings page is still on screen, so it waits for the warp to finish.
        if (chromeReturning)
        {
            void Once(object? _, EventArgs __)
            {
                ContentHost.Settled -= Once;
                ApplyChrome();
            }

            ContentHost.Settled += Once;
        }
        else
        {
            ApplyChrome();
        }

        // Cast(0) → History(1) → Settings(2): moving deeper slides in from the right, back from the left.
        int navIndex = _isSettingsOpen ? 2 : HistoryTab.IsChecked == true ? 1 : 0;
        ContentHost.SlideFrom = navIndex >= _lastNavIndex ? 36 : -36;
        ContentHost.ZoomSlide = navIndex != _lastNavIndex && navIndex != 2 && _lastNavIndex != 2;
        // Settings pours out of the gear and is sucked back into it; tabs keep the slide.
        ContentHost.Genie = navIndex == 2 && _lastNavIndex != 2 ? GenieMode.Opening
            : _lastNavIndex == 2 && navIndex != 2 ? GenieMode.Closing
            : GenieMode.None;
        _lastNavIndex = navIndex;

        ContentHost.Page = _isSettingsOpen ? _settingsView
            : HistoryTab.IsChecked == true ? _historyScreen
            : _castScreen;
    }

    private async Task<string?> PickFileAsync(string _)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, _hwnd);
        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    private async Task<string?> PickFolderAsync(string _)
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, _hwnd);
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }
}
