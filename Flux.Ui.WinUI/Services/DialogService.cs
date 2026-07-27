using System.Diagnostics;
using Flux.Ui.WinUI.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Flux.Ui.WinUI.Services;

/// <summary>Hosts the themed dialogs on the shell's XamlRoot, one at a time.</summary>
public sealed class DialogService
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Set by the shell: a ContentDialog needs a XamlRoot, which only exists once loaded.</summary>
    public Func<XamlRoot?>? XamlRootSource { get; set; }

    /// <summary>Set by the shell: unpackaged WinUI pickers must each be bound to the window handle.</summary>
    public Func<string, Task<string?>> PickFolderAsync { get; set; } = _ => Task.FromResult<string?>(null);

    /// <summary>Set by the shell: unpackaged WinUI pickers must each be bound to the window handle.</summary>
    public Func<string, string, Task<string?>> PickSaveFileAsync { get; set; } = (_, _) => Task.FromResult<string?>(null);

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

    /// <summary>Shows a yes/no confirmation; returns true only if the user confirmed.</summary>
    /// <param name="title">Dialog heading.</param>
    /// <param name="message">Dialog body.</param>
    /// <param name="destructive">When true the confirm button is styled as a destructive (red) action.</param>
    public Task<bool> ConfirmAsync(string title, string message, bool destructive = false) =>
        ShowMessageAsync(title, message, "Yes", "No", destructive);

    /// <summary>Shows an informational message with an OK button.</summary>
    public Task InformAsync(string title, string message) =>
        ShowMessageAsync(title, message, "OK", cancelText: null, destructive: false);

    /// <summary>
    /// Shows a dialog and waits for it to close. WinUI allows only one open ContentDialog, so shows
    /// queue rather than throwing; the choice is read from the dialog itself.
    /// </summary>
    public async Task ShowAsync(ContentDialog dialog)
    {
        if (XamlRootSource?.Invoke() is not { } root)
            return;

        await _gate.WaitAsync();
        try
        {
            dialog.XamlRoot = root;
            // Dialogs are hosted outside the window's content, so the chosen theme has to be carried over.
            if (root.Content is FrameworkElement content)
                dialog.RequestedTheme = content.RequestedTheme;

            await dialog.ShowAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<bool> ShowMessageAsync(
        string title, string message, string confirmText, string? cancelText, bool destructive)
    {
        var dialog = new MessageDialog(title, message, confirmText, cancelText, destructive);
        await ShowAsync(dialog);
        return dialog.Confirmed;
    }
}
