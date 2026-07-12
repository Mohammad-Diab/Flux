using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Flux.Ui.Services;
using Flux.Ui.ViewModels;
using FluxCast.Services;
using FluxCore.Ecc;
using FluxCore.Framing;
using FluxCore.Imaging;
using FluxCore.Transfer;
using Microsoft.Extensions.Logging;

namespace FluxCast.ViewModels;

/// <summary>
/// Owns navigation across the Cast and History tabs, the setup/progress/presenter flow within
/// Cast, and the title-bar Settings page.
/// </summary>
public partial class ShellViewModel : ObservableObject
{
    private readonly FluxEncodeService _encodeService;
    private readonly SourceValidator _validator;
    private readonly DialogService _dialogs;
    private readonly ILoggerFactory _loggerFactory;
    private readonly SettingsService _settings;
    private readonly ThemeService _theme;
    private readonly FluxSettings _settingsModel;
    private readonly CastHistoryService _historyService;

    private object? _castScreen;
    private RecentCastsViewModel? _recentCasts;
    private SettingsViewModel? _settingsScreen;

    /// <summary>Gets the view model shown in the shell's content host.</summary>
    [ObservableProperty]
    private object? _current;

    [ObservableProperty]
    private bool _isHistoryTab;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOpenSettings))]
    [NotifyPropertyChangedFor(nameof(ShowTabs))]
    private bool _isSettingsOpen;

    /// <summary>Gets whether the settings gear should be offered (i.e. not already on Settings).</summary>
    public bool CanOpenSettings => !IsSettingsOpen;

    /// <summary>Gets whether the tab strip is shown (hidden while the Settings page is open).</summary>
    public bool ShowTabs => !IsSettingsOpen;

    /// <summary>Gets the root directory for encode sessions.</summary>
    public static string SessionRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Flux", "FluxCast", "sessions");

    // Throwaway location for channel-test frames; kept out of the history session root.
    private static string ChannelTestRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Flux", "FluxCast", "channel-test");

    public ShellViewModel(
        FluxEncodeService encodeService,
        SourceValidator validator,
        DialogService dialogs,
        ILoggerFactory loggerFactory,
        SettingsService settings,
        ThemeService theme,
        FluxSettings settingsModel,
        CastHistoryService historyService)
    {
        _encodeService = encodeService;
        _validator = validator;
        _dialogs = dialogs;
        _loggerFactory = loggerFactory;
        _settings = settings;
        _theme = theme;
        _settingsModel = settingsModel;
        _historyService = historyService;

        ShowSetup();
    }

    /// <summary>Navigates the Cast tab to the setup screen.</summary>
    public void ShowSetup()
    {
        _castScreen = new EncodeSetupViewModel(_validator, _dialogs, StartEncode, ShowTestFrame, DisplayMetrics.PrimaryScreenPixels());
        UpdateCurrent();
    }

    partial void OnIsHistoryTabChanged(bool value)
    {
        if (value)
        {
            _recentCasts ??= new RecentCastsViewModel(_historyService, _dialogs, SessionRoot, ResumeCast);
            _recentCasts.Refresh();
        }

        if (!IsSettingsOpen)
            UpdateCurrent();
    }

    /// <summary>Opens Settings; the title-bar back button returns to the current tab.</summary>
    [RelayCommand]
    private void ShowSettings()
    {
        _settingsScreen ??= new SettingsViewModel(_settings, _theme, _settingsModel);
        IsSettingsOpen = true;
        UpdateCurrent();
    }

    /// <summary>Returns from Settings to the tab shown before it was opened.</summary>
    [RelayCommand]
    private void CloseSettings()
    {
        IsSettingsOpen = false;
        UpdateCurrent();
    }

    private void ResumeCast(CastHistoryEntry entry)
    {
        var session = _historyService.OpenForPresenting(entry.SessionDirectory);
        _castScreen = new PresenterViewModel(session, ShowSetup);
        IsSettingsOpen = false;
        IsHistoryTab = false;
        UpdateCurrent();
    }

    private void StartEncode(string sourcePath, EncodeOptions options)
    {
        _castScreen = new EncodeProgressViewModel(
            _encodeService, sourcePath, SessionRoot, options,
            onCompleted: ShowPresenter,
            onCancelledOrFailed: ShowSetup,
            _loggerFactory.CreateLogger<EncodeProgressViewModel>());
        UpdateCurrent();
    }

    private void ShowPresenter(EncodeSessionResult session)
    {
        _castScreen = new PresenterViewModel(session, ShowSetup);
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
        var source = PrepareTestSource(testOptions);
        _castScreen = new EncodeProgressViewModel(
            _encodeService, source, ChannelTestRoot, testOptions,
            onCompleted: ShowPresenter, onCancelledOrFailed: ShowSetup,
            _loggerFactory.CreateLogger<EncodeProgressViewModel>());
        IsSettingsOpen = false;
        IsHistoryTab = false;
        UpdateCurrent();
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

    private void UpdateCurrent() =>
        Current = IsSettingsOpen ? _settingsScreen
            : IsHistoryTab ? _recentCasts
            : _castScreen;
}
