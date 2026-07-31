using CommunityToolkit.Mvvm.ComponentModel;
using Flux.Ui.Services;
using Flux.Ui.Controls;

namespace Flux.Ui.ViewModels;

/// <summary>Settings screen: appearance and performance, each applied live and saved immediately.</summary>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly FluxSettings _model;
    private readonly Action<AppThemeMode> _applyTheme;

    public SettingsViewModel(SettingsService settings, FluxSettings model, Action<AppThemeMode> applyTheme)
    {
        _settings = settings;
        _model = model;
        _applyTheme = applyTheme;
        MotionSettings.Current.UserPrefers = !model.PerformanceMode;
    }

    public bool IsSystem { get => _model.ThemeMode == AppThemeMode.System; set { if (value) SetTheme(AppThemeMode.System); } }

    public bool IsLight { get => _model.ThemeMode == AppThemeMode.Light; set { if (value) SetTheme(AppThemeMode.Light); } }

    public bool IsDark { get => _model.ThemeMode == AppThemeMode.Dark; set { if (value) SetTheme(AppThemeMode.Dark); } }

    /// <summary>When on, skips animations and expensive visual effects.</summary>
    public bool PerformanceMode
    {
        get => _model.PerformanceMode;
        set
        {
            if (_model.PerformanceMode == value)
                return;
            _model.PerformanceMode = value;
            MotionSettings.Current.UserPrefers = !value;
            _settings.Save(_model);
            OnPropertyChanged();
        }
    }

    private void SetTheme(AppThemeMode mode)
    {
        if (_model.ThemeMode == mode)
            return;
        _model.ThemeMode = mode;
        _applyTheme(mode);
        _settings.Save(_model);
        OnPropertyChanged(nameof(IsSystem));
        OnPropertyChanged(nameof(IsLight));
        OnPropertyChanged(nameof(IsDark));
    }
}
