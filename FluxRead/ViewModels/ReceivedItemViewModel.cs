using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Flux.Ui;
using FluxCore.Framing;
using FluxCore.Transfer;

namespace FluxRead.ViewModels;

/// <summary>One reception in the history list, with its row actions.</summary>
public partial class ReceivedItemViewModel : ObservableObject
{
    private readonly Action<ReceivedItemViewModel> _resume;
    private readonly Action<ReceivedItemViewModel> _openLocation;
    private readonly Action<ReceivedItemViewModel> _delete;

    public ReceivedItemViewModel(
        ReceptionEntry entry,
        Action<ReceivedItemViewModel> resume,
        Action<ReceivedItemViewModel> openLocation,
        Action<ReceivedItemViewModel> delete)
    {
        Entry = entry;
        _resume = resume;
        _openLocation = openLocation;
        _delete = delete;
    }

    /// <summary>Gets the underlying reception entry.</summary>
    public ReceptionEntry Entry { get; }

    /// <summary>Gets the received item's name.</summary>
    public string DisplayName => Entry.DisplayName;

    /// <summary>Gets the Segoe MDL2 glyph for the payload type (archive/folder vs single file).</summary>
    public string TypeGlyph => Entry.PayloadType == PayloadType.Raw ? "" : "";

    /// <summary>Gets the frame-count column text (received/total while incomplete).</summary>
    public string FramesText => Entry.IsComplete
        ? $"{Entry.ExpectedPayloadFrames:N0} frames"
        : $"{Entry.ReceivedFrames:N0} / {Entry.ExpectedPayloadFrames:N0} frames";

    /// <summary>Gets the size column text (the original, uncompressed size when known).</summary>
    public string SizeText => ByteFormat.Bytes(Entry.OriginalLength > 0 ? Entry.OriginalLength : Entry.PayloadLength);

    /// <summary>Gets the relative-date column text.</summary>
    public string DateText => TimeFormat.Relative(Entry.CompletedUtc ?? Entry.CreatedUtc);

    /// <summary>Gets the completion status word.</summary>
    public string StatusText => Entry.IsComplete ? "Complete" : "Incomplete";

    /// <summary>Gets the saved-location text (or resume prompt while incomplete).</summary>
    public string LocationText => Entry.IsComplete
        ? Entry.SavedPath ?? "Saved location not recorded"
        : "Not saved yet — resume to finish";

    /// <summary>Gets whether this reception can be resumed (it isn't complete yet).</summary>
    public bool CanResume => !Entry.IsComplete;

    /// <summary>Gets whether the saved output still exists to open.</summary>
    public bool CanOpenLocation =>
        Entry.SavedPath is { } path && (File.Exists(path) || Directory.Exists(path));

    /// <summary>Gets the tooltip for the Resume button.</summary>
    public string ResumeTooltip => CanResume
        ? "Switch to live capture and continue this reception"
        : "This reception is already complete";

    /// <summary>Gets the tooltip for the Open-location button.</summary>
    public string OpenLocationTooltip => CanOpenLocation
        ? "Reveal the saved file or open the saved folder"
        : "The saved output is no longer available";

    [RelayCommand(CanExecute = nameof(CanResume))]
    private void Resume() => _resume(this);

    [RelayCommand(CanExecute = nameof(CanOpenLocation))]
    private void OpenLocation() => _openLocation(this);

    [RelayCommand]
    private void Delete() => _delete(this);
}
