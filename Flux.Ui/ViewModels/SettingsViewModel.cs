using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Flux.Ui.Controls;
using Flux.Ui.Services;

namespace Flux.Ui.ViewModels;

/// <summary>
/// Backs the Settings screen: appearance (System/Light/Dark) and motion. Each change is applied
/// live and persisted immediately.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly ThemeService _theme;
    private readonly FluxSettings _model;
    private readonly Action? _onDone;

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsViewModel"/> class.
    /// </summary>
    /// <param name="settings">Persistence service.</param>
    /// <param name="theme">Theme applier.</param>
    /// <param name="model">Shared, already-loaded settings instance.</param>
    /// <param name="onDone">Optional "done" callback (shown as a button when provided).</param>
    public SettingsViewModel(SettingsService settings, ThemeService theme, FluxSettings model, Action? onDone = null)
    {
        _settings = settings;
        _theme = theme;
        _model = model;
        _onDone = onDone;
    }

    /// <summary>Whether the Done button is shown (page mode) versus hidden (tab mode).</summary>
    public bool ShowDone => _onDone is not null;

    public bool IsSystem { get => _model.ThemeMode == AppThemeMode.System; set { if (value) SetTheme(AppThemeMode.System); } }
    public bool IsLight { get => _model.ThemeMode == AppThemeMode.Light; set { if (value) SetTheme(AppThemeMode.Light); } }
    public bool IsDark { get => _model.ThemeMode == AppThemeMode.Dark; set { if (value) SetTheme(AppThemeMode.Dark); } }

    /// <summary>User's animation preference; false skips transitions and effects.</summary>
    public bool EnableAnimations
    {
        get => _model.EnableAnimations;
        set
        {
            if (_model.EnableAnimations == value)
                return;
            _model.EnableAnimations = value;
            MotionSettings.Current.UserEnableAnimations = value;
            _settings.Save(_model);
            OnPropertyChanged();
        }
    }

    [RelayCommand]
    private void Done() => _onDone?.Invoke();

    private void SetTheme(AppThemeMode mode)
    {
        if (_model.ThemeMode == mode)
            return;
        _model.ThemeMode = mode;
        _theme.Apply(mode);
        _settings.Save(_model);
        OnPropertyChanged(nameof(IsSystem));
        OnPropertyChanged(nameof(IsLight));
        OnPropertyChanged(nameof(IsDark));
    }
}
