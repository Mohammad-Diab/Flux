using Microsoft.Win32;

namespace FluxRead.Services;

/// <summary>
/// Thin wrapper over the WPF folder and save-file pickers.
/// </summary>
public sealed class DialogService
{
    /// <summary>Shows a folder picker for the frames folder; returns null when cancelled.</summary>
    public string? PickFramesFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Choose the folder of frame images" };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    /// <summary>Shows a save-file picker seeded with a suggested name; returns null when cancelled.</summary>
    /// <param name="suggestedName">Default file name.</param>
    public string? PickSaveFile(string suggestedName)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save decoded file",
            FileName = suggestedName,
            OverwritePrompt = true,
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    /// <summary>Shows a folder picker for the extraction destination; returns null when cancelled.</summary>
    public string? PickOutputFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Choose a folder to extract into" };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
}
