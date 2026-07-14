using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Flux.Ui;
using FluxCore.Imaging;
using FluxCore.Transfer;

namespace FluxCast.ViewModels;

/// <summary>One cast in the history list, with its row actions.</summary>
public partial class CastHistoryItemViewModel : ObservableObject
{
    private readonly Action<CastHistoryItemViewModel> _resume;
    private readonly Action<CastHistoryItemViewModel> _delete;
    private readonly Action<CastHistoryItemViewModel> _openLocation;
    private readonly Action<CastHistoryItemViewModel> _openFrames;
    private readonly Action<CastHistoryItemViewModel> _exportFrames;

    public CastHistoryItemViewModel(
        CastHistoryEntry entry,
        Action<CastHistoryItemViewModel> resume,
        Action<CastHistoryItemViewModel> delete,
        Action<CastHistoryItemViewModel> openLocation,
        Action<CastHistoryItemViewModel> openFrames,
        Action<CastHistoryItemViewModel> exportFrames)
    {
        Entry = entry;
        _resume = resume;
        _delete = delete;
        _openLocation = openLocation;
        _openFrames = openFrames;
        _exportFrames = exportFrames;
    }

    /// <summary>Gets the underlying history entry.</summary>
    public CastHistoryEntry Entry { get; }

    /// <summary>Gets the source name.</summary>
    public string DisplayName => Entry.DisplayName;

    /// <summary>Gets the render-spec label distinguishing this variant (grid, ECC, and colour/rugged when not the 256 default).</summary>
    public string SpecText => Entry.PaletteKind == PaletteKind.Rugged
        ? $"{Entry.GridWidthTiles}×{Entry.GridHeightTiles} · {Entry.EccLevel} ECC · rugged"
        : Entry.ColorCount == 256
            ? $"{Entry.GridWidthTiles}×{Entry.GridHeightTiles} · {Entry.EccLevel} ECC"
            : $"{Entry.GridWidthTiles}×{Entry.GridHeightTiles} · {Entry.EccLevel} ECC · {Entry.ColorCount} colours";

    /// <summary>Gets the Segoe MDL2 glyph for the source type (folder vs file).</summary>
    public string TypeGlyph => Entry.SourceKind == SourceKind.Folder ? "" : "";

    /// <summary>Gets the frame-count column text.</summary>
    public string FramesText => $"{Entry.TotalFrames:N0} frames";

    /// <summary>Gets the payload-size column text.</summary>
    public string SizeText => ByteFormat.Bytes(Entry.PayloadLength);

    /// <summary>Gets the relative-date column text.</summary>
    public string DateText => TimeFormat.Relative(Entry.CreatedUtc);

    /// <summary>Gets the original source path (or a placeholder when unrecorded).</summary>
    public string LocationText => Entry.SourcePath ?? "Source location not recorded";

    /// <summary>Gets whether the cast can be re-presented (all frames present on disk).</summary>
    public bool CanResume => Entry.IsComplete;

    /// <summary>Gets whether the source still exists on disk to open.</summary>
    public bool CanOpenLocation =>
        Entry.SourcePath is { } path && (File.Exists(path) || Directory.Exists(path));

    /// <summary>Gets the tooltip for the Resume button.</summary>
    public string ResumeTooltip => CanResume
        ? "Re-open the presenter from the first frame"
        : "Encoding was interrupted — re-select the source to finish it";

    /// <summary>Gets the tooltip for the Open-location button.</summary>
    public string OpenLocationTooltip => CanOpenLocation
        ? "Reveal the source file or open the source folder"
        : "The source is no longer available";

    [RelayCommand(CanExecute = nameof(CanResume))]
    private void Resume() => _resume(this);

    [RelayCommand]
    private void Delete() => _delete(this);

    [RelayCommand(CanExecute = nameof(CanOpenLocation))]
    private void OpenLocation() => _openLocation(this);

    [RelayCommand]
    private void OpenFrames() => _openFrames(this);

    [RelayCommand]
    private void ExportFrames() => _exportFrames(this);
}
