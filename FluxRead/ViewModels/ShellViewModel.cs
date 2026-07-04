using CommunityToolkit.Mvvm.ComponentModel;

namespace FluxRead.ViewModels;

/// <summary>
/// Owns the current screen. v1 shows folder-decode mode; the live optical mode is added in a
/// later phase, at which point this becomes a mode switcher.
/// </summary>
public partial class ShellViewModel : ObservableObject
{
    [ObservableProperty]
    private object _current;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShellViewModel"/> class.
    /// </summary>
    /// <param name="folderDecode">The folder-decode screen.</param>
    public ShellViewModel(FolderDecodeViewModel folderDecode)
    {
        _current = folderDecode;
    }
}
