using System.ComponentModel;
using Windows.UI.ViewManagement;

namespace FluxRead.WinUI.Controls;

/// <summary>The gate every animation checks — the user's preference and the system animation setting.</summary>
public sealed class MotionSettings : INotifyPropertyChanged
{
    public static MotionSettings Current { get; } = new();

    private readonly bool _systemAllows;
    private bool _userPrefers = true;

    private MotionSettings() => _systemAllows = new UISettings().AnimationsEnabled;

    /// <summary>The user's preference; persisted by the settings screen.</summary>
    public bool UserPrefers
    {
        get => _userPrefers;
        set
        {
            if (_userPrefers == value)
                return;
            _userPrefers = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UserPrefers)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AnimationsEnabled)));
        }
    }

    /// <summary>Whether animations should play at all.</summary>
    public bool AnimationsEnabled => _userPrefers && _systemAllows;

    public event PropertyChangedEventHandler? PropertyChanged;
}
