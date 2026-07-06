using System;
using System.ComponentModel;
using System.Windows;

namespace Flux.Ui.Controls;

/// <summary>
/// Central gate for UI motion. Animations run only when the user hasn't turned them off AND
/// Windows' "animate controls and elements" preference is on. Registered as an application
/// resource (key "MotionSettings") so XAML triggers and code-behind share one instance.
/// </summary>
public sealed class MotionSettings : INotifyPropertyChanged
{
    private static readonly Lazy<MotionSettings> Fallback = new(() => new MotionSettings());
    private bool _userEnabled = true;

    public MotionSettings()
    {
        // React when the OS "animate controls and elements inside windows" preference changes.
        SystemParameters.StaticPropertyChanged += (_, e) =>
        {
            if (e.PropertyName is null or nameof(SystemParameters.ClientAreaAnimation))
                Raise();
        };
    }

    /// <summary>The shared instance held in application resources.</summary>
    public static MotionSettings Current =>
        Application.Current?.TryFindResource("MotionSettings") as MotionSettings ?? Fallback.Value;

    /// <summary>User's explicit preference; false means "reduce motion".</summary>
    public bool UserEnableAnimations
    {
        get => _userEnabled;
        set { if (_userEnabled != value) { _userEnabled = value; Raise(); } }
    }

    /// <summary>Effective flag: user preference AND the Windows system animation preference.</summary>
    public bool AnimationsEnabled => _userEnabled && SystemParameters.ClientAreaAnimation;

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AnimationsEnabled)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UserEnableAnimations)));
    }
}
