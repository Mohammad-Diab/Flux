using Microsoft.Win32;

namespace FluxCast.Services;

/// <summary>
/// Thin wrapper over the WPF file and folder pickers.
/// </summary>
public sealed class DialogService
{
    /// <summary>Shows a file picker and returns the chosen path, or null when cancelled.</summary>
    public string? PickFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose a file to transfer",
            CheckFileExists = true,
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    /// <summary>Shows a folder picker and returns the chosen path, or null when cancelled.</summary>
    public string? PickFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose a folder to transfer",
        };

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
}
