using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using FluxCore.Transfer;
using Flux.Ui.WinUI.Services;
using FluxRead.WinUI.Services;

namespace FluxRead.WinUI.ViewModels;

/// <summary>History screen: received and partially received transfers, with resume, open, and delete.</summary>
public partial class ReceivedItemsViewModel : ObservableObject
{
    private readonly ReceptionHistoryService _history;
    private readonly DialogService _dialogs;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasItems))]
    private bool _isEmpty = true;

    /// <summary>Gets the receptions shown in the list, most recent first.</summary>
    public ObservableCollection<ReceivedItemViewModel> Items { get; } = [];

    /// <summary>Gets whether there is anything to list.</summary>
    public bool HasItems => !IsEmpty;

    /// <summary>Invoked when the user asks to resume a reception; the shell switches to live capture.</summary>
    public Action? ResumeRequested { get; set; }

    public ReceivedItemsViewModel(ReceptionHistoryService history, DialogService dialogs)
    {
        _history = history;
        _dialogs = dialogs;
    }

    /// <summary>Reloads the list from disk.</summary>
    public void Refresh()
    {
        Items.Clear();
        foreach (var entry in _history.List(ReceptionPaths.SessionRoot))
            Items.Add(new ReceivedItemViewModel(entry, Resume, OpenLocation, item => _ = DeleteAsync(item)));
        IsEmpty = Items.Count == 0;
    }

    private void Resume(ReceivedItemViewModel item) => ResumeRequested?.Invoke();

    private void OpenLocation(ReceivedItemViewModel item)
    {
        if (item.Entry.SavedPath is { } path)
            _dialogs.OpenInExplorer(path);
    }

    private async Task DeleteAsync(ReceivedItemViewModel item)
    {
        string message = item.Entry.IsComplete
            ? $"Remove “{item.DisplayName}” from history? The saved output stays; this only clears the record."
            : $"Discard the partial reception “{item.DisplayName}” and free its disk space? This can't be undone.";
        if (!await _dialogs.ConfirmAsync("Delete reception", message, destructive: true))
            return;

        try
        {
            _history.Delete(item.Entry.SessionDirectory);
            Items.Remove(item);
            IsEmpty = Items.Count == 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _dialogs.InformAsync("Couldn't delete", "The reception is in use. Try again in a moment.");
        }
    }
}
