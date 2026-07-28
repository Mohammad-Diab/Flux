using System.IO;
using Flux.Ui.Services;
using Flux.Ui.WinUI.Services;
using Flux.Ui.WinUI.ViewModels;
using Flux.Ui.WinUI.Views;
using FluxCast.Services;
using FluxCast.WinUI.Services;
using FluxCast.WinUI.ViewModels;
using FluxCast.WinUI.Views;
using FluxCore.Compression;
using FluxCore.Ecc;
using FluxCore.Framing;
using FluxCore.Imaging;
using FluxCore.Transfer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace FluxCast.WinUI;

/// <summary>
/// Owns navigation across the Cast and History tabs, the setup/progress/presenter flow within Cast,
/// and the title-bar Settings page. WPF drove this from a ShellViewModel over typed DataTemplates;
/// WinUI has no implicit template-by-type, so the shell holds the views directly.
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly IntPtr _hwnd;
    private readonly DialogService _dialogs;
    private readonly SettingsView _settingsView;

    private object? _castScreen;
    private RecentCastsView? _historyScreen;
    private bool _isSettingsOpen;
    private int _lastNavIndex;

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
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarStrip);
        Ambient.Attach(this);

        _hwnd = WindowNative.GetWindowHandle(this);
        Flux.Ui.WinUI.Interop.TaskbarProgress.Current.Attach(_hwnd);

        // Unpackaged WinUI pickers have no implicit parent window, so each one is bound to our HWND.
        _dialogs.PickFileAsync = PickFileAsync;
        _dialogs.PickFolderAsync = PickFolderAsync;
        _dialogs.XamlRootSource = () => Content?.XamlRoot;

        _settingsView = new SettingsView(App.Services.GetRequiredService<SettingsViewModel>());
        ShowSetup();
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
        TabStrip.Visibility = _isSettingsOpen || presenting ? Visibility.Collapsed : Visibility.Visible;
        SettingsButton.Visibility = _isSettingsOpen || presenting ? Visibility.Collapsed : Visibility.Visible;
        BackButton.Visibility = _isSettingsOpen || presenting ? Visibility.Visible : Visibility.Collapsed;

        // Cast(0) → History(1) → Settings(2): moving deeper slides in from the right, back from the left.
        int navIndex = _isSettingsOpen ? 2 : HistoryTab.IsChecked == true ? 1 : 0;
        ContentHost.SlideFrom = navIndex >= _lastNavIndex ? 36 : -36;
        ContentHost.ZoomSlide = navIndex != _lastNavIndex && navIndex != 2 && _lastNavIndex != 2;
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
