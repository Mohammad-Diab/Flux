using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Flux.Ui.Services;
using FluxCore.Compression;
using FluxCore.Transfer;

namespace FluxCast.ViewModels;

/// <summary>History screen: past casts with resume, open-location, export, and delete.</summary>
public partial class RecentCastsViewModel : ObservableObject
{
    private readonly CastHistoryService _history;
    private readonly DialogService _dialogs;
    private readonly CompressionService _compression;
    private readonly string _sessionRoot;
    private readonly Action<CastHistoryEntry> _onResume;

    private CastHistoryItemViewModel? _pendingExport;

    [ObservableProperty]
    private bool _isEmpty = true;

    [ObservableProperty]
    private bool _isExportPromptOpen;

    [ObservableProperty]
    private bool _compressExport;

    [ObservableProperty]
    private string _exportPromptTitle = "";

    /// <summary>Gets the casts shown in the list, newest first.</summary>
    public ObservableCollection<CastHistoryItemViewModel> Casts { get; } = [];

    public RecentCastsViewModel(
        CastHistoryService history, DialogService dialogs, CompressionService compression,
        string sessionRoot, Action<CastHistoryEntry> onResume)
    {
        _history = history;
        _dialogs = dialogs;
        _compression = compression;
        _sessionRoot = sessionRoot;
        _onResume = onResume;
    }

    /// <summary>Reloads the list from disk.</summary>
    public void Refresh()
    {
        Casts.Clear();
        foreach (var entry in Cluster(_history.List(_sessionRoot)))
            Casts.Add(new CastHistoryItemViewModel(entry, Resume, Delete, OpenLocation, OpenFrames, ExportFrames));
        IsEmpty = Casts.Count == 0;
    }

    // Keep render variants of one payload adjacent (payload dir = the render folder's grandparent),
    // with the most recently touched payload first.
    private static IEnumerable<CastHistoryEntry> Cluster(IReadOnlyList<CastHistoryEntry> entries) =>
        entries
            .GroupBy(e => Path.GetDirectoryName(Path.GetDirectoryName(e.SessionDirectory)))
            .OrderByDescending(g => g.Max(e => e.CreatedUtc))
            .SelectMany(g => g.OrderByDescending(e => e.CreatedUtc));

    private void Resume(CastHistoryItemViewModel item) => _onResume(item.Entry);

    private void Delete(CastHistoryItemViewModel item)
    {
        if (!_dialogs.Confirm(
                "Delete cast",
                $"Delete this rendering of “{item.DisplayName}” ({item.SpecText}) and its frames from disk? This frees space and can't be undone."))
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

    private void ExportFrames(CastHistoryItemViewModel item)
    {
        _pendingExport = item;
        ExportPromptTitle = $"Export “{item.DisplayName}”";
        CompressExport = false;
        IsExportPromptOpen = true;
    }

    [RelayCommand]
    private void CancelExport()
    {
        IsExportPromptOpen = false;
        _pendingExport = null;
    }

    [RelayCommand]
    private async Task ConfirmExport()
    {
        if (_pendingExport is not { } item)
            return;

        IsExportPromptOpen = false;
        bool compress = CompressExport;
        _pendingExport = null;

        var destination = _dialogs.PickFolder("Choose a folder to export the frames into");
        if (destination is null)
            return;

        try
        {
            var revealed = await RunExportAsync(item, destination, compress);
            _dialogs.OpenInExplorer(revealed);
        }
        catch (InvalidOperationException)
        {
            _dialogs.Inform("Nothing to export", "This cast has no frames on disk to export.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CompressionException)
        {
            _dialogs.Inform("Export failed", $"The frames couldn't be exported: {ex.Message}");
        }
    }

    // Copies the frames into a subfolder of the chosen destination; when compressing, packs that
    // subfolder into a single max-compression .7z beside it and removes the loose folder. Returns the
    // path to reveal (the folder or the archive).
    private async Task<string> RunExportAsync(CastHistoryItemViewModel item, string destination, bool compress)
    {
        var entry = item.Entry;
        var info = new FrameExportInfo(
            entry.DisplayName, entry.SourceKind, entry.TotalFrames,
            entry.GridWidthTiles, entry.GridHeightTiles, entry.EccLevel, entry.ColorCount, entry.CreatedUtc);
        var baseName = $"{entry.DisplayName} - {entry.GridWidthTiles}x{entry.GridHeightTiles} {entry.EccLevel} ECC";

        var export = await Task.Run(() =>
            FrameExporter.ExportToFolder(entry.FramesDirectory, destination, baseName, info, DateTimeOffset.UtcNow));

        if (!compress)
            return export.OutputDirectory;

        var archive = await _compression.CompressAsync(export.OutputDirectory);
        var archivePath = UniqueArchivePath(destination, Path.GetFileName(export.OutputDirectory));
        await File.WriteAllBytesAsync(archivePath, archive.Data);
        try { Directory.Delete(export.OutputDirectory, recursive: true); } catch { /* loose copy is harmless if it lingers */ }
        return archivePath;
    }

    private static string UniqueArchivePath(string root, string name)
    {
        var path = Path.Combine(root, $"{name}.7z");
        for (int n = 2; File.Exists(path); n++)
            path = Path.Combine(root, $"{name} ({n}).7z");
        return path;
    }
}
