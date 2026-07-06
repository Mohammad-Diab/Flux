using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Flux.Ui.Services;
using Flux.Ui.ViewModels;
using FluxCast.Services;
using FluxCore.Transfer;
using Microsoft.Extensions.Logging;

namespace FluxCast.ViewModels;

/// <summary>
/// Owns navigation between the setup, progress, presenter, and settings screens.
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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSettingsOpen))]
    [NotifyPropertyChangedFor(nameof(CanOpenSettings))]
    private object? _current;

    private object? _beforeSettings;

    /// <summary>Whether the Settings page is currently shown.</summary>
    public bool IsSettingsOpen => Current is SettingsViewModel;

    /// <summary>Whether the settings gear should be offered (i.e. not already on Settings).</summary>
    public bool CanOpenSettings => !IsSettingsOpen;

    /// <summary>Gets the root directory for encode sessions.</summary>
    public static string SessionRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Flux", "FluxCast", "sessions");

    public ShellViewModel(
        FluxEncodeService encodeService,
        SourceValidator validator,
        DialogService dialogs,
        ILoggerFactory loggerFactory,
        SettingsService settings,
        ThemeService theme,
        FluxSettings settingsModel)
    {
        _encodeService = encodeService;
        _validator = validator;
        _dialogs = dialogs;
        _loggerFactory = loggerFactory;
        _settings = settings;
        _theme = theme;
        _settingsModel = settingsModel;

        ShowSetup();
    }

    /// <summary>Navigates to the setup screen.</summary>
    public void ShowSetup() =>
        Current = new EncodeSetupViewModel(_validator, _dialogs, StartEncode);

    /// <summary>Opens Settings; the title-bar back button returns to the previous screen.</summary>
    [RelayCommand]
    private void ShowSettings()
    {
        if (Current is SettingsViewModel)
            return;
        _beforeSettings = Current;
        Current = new SettingsViewModel(_settings, _theme, _settingsModel);
    }

    /// <summary>Returns from Settings to the screen shown before it was opened.</summary>
    [RelayCommand]
    private void CloseSettings()
    {
        if (_beforeSettings is not null)
            Current = _beforeSettings;
    }

    private void StartEncode(string sourcePath, EncodeOptions options) =>
        Current = new EncodeProgressViewModel(
            _encodeService, sourcePath, SessionRoot, options,
            onCompleted: ShowPresenter,
            onCancelledOrFailed: ShowSetup,
            _loggerFactory.CreateLogger<EncodeProgressViewModel>());

    private void ShowPresenter(EncodeSessionResult session) =>
        Current = new PresenterViewModel(session, ShowSetup);
}
