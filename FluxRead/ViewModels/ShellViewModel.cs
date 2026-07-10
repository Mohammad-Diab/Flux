using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FluxRead.ViewModels;

/// <summary>
/// Tracks whether the title-bar Settings page is shown; the window swaps content in response.
/// </summary>
public partial class ShellViewModel : ObservableObject
{
    /// <summary>Root folder holding per-reception session directories.</summary>
    public static string SessionRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Flux", "FluxRead", "sessions");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOpenSettings))]
    private bool _isSettingsOpen;

    /// <summary>Whether the settings gear should be offered (i.e. not already on Settings).</summary>
    public bool CanOpenSettings => !IsSettingsOpen;

    [RelayCommand]
    private void ShowSettings() => IsSettingsOpen = true;

    [RelayCommand]
    private void CloseSettings() => IsSettingsOpen = false;
}
