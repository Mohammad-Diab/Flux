using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Flux.Ui.Services;
using FluxCore.Transfer;

namespace FluxCast.ViewModels;

/// <summary>History screen: past casts with resume, open-location, and delete.</summary>
public partial class RecentCastsViewModel : ObservableObject
{
    private readonly CastHistoryService _history;
    private readonly DialogService _dialogs;
    private readonly string _sessionRoot;
    private readonly Action<CastHistoryEntry> _onResume;

    [ObservableProperty]
    private bool _isEmpty = true;

    /// <summary>Gets the casts shown in the list, newest first.</summary>
    public ObservableCollection<CastHistoryItemViewModel> Casts { get; } = [];

    public RecentCastsViewModel(
        CastHistoryService history, DialogService dialogs, string sessionRoot, Action<CastHistoryEntry> onResume)
    {
        _history = history;
        _dialogs = dialogs;
        _sessionRoot = sessionRoot;
        _onResume = onResume;
    }

    /// <summary>Reloads the list from disk.</summary>
    public void Refresh()
    {
        Casts.Clear();
        foreach (var entry in _history.List(_sessionRoot))
            Casts.Add(new CastHistoryItemViewModel(entry, Resume, Delete, OpenLocation, OpenFrames));
        IsEmpty = Casts.Count == 0;
    }

    private void Resume(CastHistoryItemViewModel item) => _onResume(item.Entry);

    private void Delete(CastHistoryItemViewModel item)
    {
        if (!_dialogs.Confirm(
                "Delete cast",
                $"Delete “{item.DisplayName}” and its frames from disk? This frees space and can't be undone."))
            return;

        try
        {
            _history.Delete(item.Entry.SessionDirectory);
            Casts.Remove(item);
            IsEmpty = Casts.Count == 0;
        }
        catch (IOException)
        {
            _dialogs.Inform("Couldn't delete", "The cast is in use. Close the presenter and try again.");
        }
        catch (UnauthorizedAccessException)
        {
            _dialogs.Inform("Couldn't delete", "The cast is in use. Close the presenter and try again.");
        }
    }

    private void OpenLocation(CastHistoryItemViewModel item)
    {
        if (item.Entry.SourcePath is { } path)
            _dialogs.OpenInExplorer(path);
    }

    private void OpenFrames(CastHistoryItemViewModel item) =>
        _dialogs.OpenInExplorer(item.Entry.FramesDirectory);
}
