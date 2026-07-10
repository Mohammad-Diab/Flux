using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Flux.Ui.Services;
using FluxCore.Transfer;

namespace FluxRead.ViewModels;

/// <summary>History screen: received and partially received transfers, with resume, open, and delete.</summary>
public partial class ReceivedItemsViewModel : ObservableObject
{
    private readonly ReceptionHistoryService _history;
    private readonly DialogService _dialogs;

    [ObservableProperty]
    private bool _isEmpty = true;

    /// <summary>Gets the receptions shown in the list, most recent first.</summary>
    public ObservableCollection<ReceivedItemViewModel> Items { get; } = [];

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
        foreach (var entry in _history.List(ShellViewModel.SessionRoot))
            Items.Add(new ReceivedItemViewModel(entry, Resume, OpenLocation, Delete));
        IsEmpty = Items.Count == 0;
    }

    private void Resume(ReceivedItemViewModel item) => ResumeRequested?.Invoke();

    private void OpenLocation(ReceivedItemViewModel item)
    {
        if (item.Entry.SavedPath is { } path)
            _dialogs.OpenInExplorer(path);
    }

    private void Delete(ReceivedItemViewModel item)
    {
        string message = item.Entry.IsComplete
            ? $"Remove “{item.DisplayName}” from history? The saved output stays; this only clears the record."
            : $"Discard the partial reception “{item.DisplayName}” and free its disk space? This can't be undone.";
        if (!_dialogs.Confirm("Delete reception", message))
            return;

        try
        {
            _history.Delete(item.Entry.SessionDirectory);
            Items.Remove(item);
            IsEmpty = Items.Count == 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.Inform("Couldn't delete", "The reception is in use. Try again in a moment.");
        }
    }
}
