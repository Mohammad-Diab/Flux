using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace Flux.Ui.Services;

/// <summary>Thin wrapper over the WPF file/folder pickers and the Explorer reveal.</summary>
public sealed class DialogService
{
    public string? PickFile(string title)
    {
        var dialog = new OpenFileDialog { Title = title, CheckFileExists = true };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickFolder(string title)
    {
        var dialog = new OpenFolderDialog { Title = title };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    public string? PickSaveFile(string title, string suggestedName)
    {
        var dialog = new SaveFileDialog
        {
            Title = title,
            FileName = suggestedName,
            OverwritePrompt = true,
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    /// <summary>
    /// Reveals a saved result in File Explorer — selects the file if <paramref name="path"/> is a
    /// file, or opens the folder if it's a directory. Best-effort; failures are ignored.
    /// </summary>
    public void OpenInExplorer(string path)
    {
        try
        {
            if (File.Exists(path))
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            else if (Directory.Exists(path))
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch
        {
            // Opening Explorer is a convenience; don't fail the save over it.
        }
    }
}
