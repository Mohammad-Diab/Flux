using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Flux.Ui;
using Flux.Ui.Services;
using FluxCast.Services;
using FluxCore.Ecc;
using FluxCore.Transfer;

namespace FluxCast.ViewModels;

/// <summary>An ECC level choice presented in the setup screen.</summary>
public sealed record EccChoice(EccLevel Level, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// Setup screen: pick a file or folder, validate it, choose options, start encoding.
/// </summary>
public partial class EncodeSetupViewModel : ObservableObject
{
    private readonly SourceValidator _validator;
    private readonly DialogService _dialogs;
    private readonly Action<string, EncodeOptions> _onStart;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private string? _selectedPath;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private SourceInfo? _sourceInfo;

    [ObservableProperty]
    private bool _isValidating;

    [ObservableProperty]
    private EccChoice _selectedEccLevel;

    [ObservableProperty]
    private bool _compress = true;

    [ObservableProperty]
    private bool _compressLocked;

    /// <summary>Gets the selectable ECC levels.</summary>
    public IReadOnlyList<EccChoice> EccLevels { get; } =
    [
        new(EccLevel.Low, "Low — 11.8 KB/frame, clean captures only"),
        new(EccLevel.Medium, "Medium — 10.1 KB/frame (recommended)"),
        new(EccLevel.High, "High — 8.4 KB/frame, lossy channels"),
        new(EccLevel.Max, "Max — 6.7 KB/frame, worst channels"),
    ];

    /// <summary>Gets a summary of the selected source for display.</summary>
    public string SourceSummary => SourceInfo switch
    {
        null => "",
        { Error: not null } info => info.Error,
        { IsFolder: true } info => $"Folder — {info.FileCount:N0} files, {ByteFormat.Bytes(info.TotalBytes)}",
        var info => $"File — {ByteFormat.Bytes(info.TotalBytes)}",
    };

    /// <summary>Gets the source name, type, and modified time for the details panel.</summary>
    public string SourceDetails { get; private set; } = "";

    /// <summary>Gets the estimated frame count / Next-click count for the details panel.</summary>
    public string EstimatedFrames { get; private set; } = "";

    public EncodeSetupViewModel(SourceValidator validator, DialogService dialogs, Action<string, EncodeOptions> onStart)
    {
        _validator = validator;
        _dialogs = dialogs;
        _onStart = onStart;
        _selectedEccLevel = EccLevels[1];
    }

    [RelayCommand]
    private async Task PickFileAsync()
    {
        var path = _dialogs.PickFile("Choose a file to transfer");
        if (path is not null)
            await SelectAsync(path, isFolder: false);
    }

    [RelayCommand]
    private async Task PickFolderAsync()
    {
        var path = _dialogs.PickFolder("Choose a folder to transfer");
        if (path is not null)
            await SelectAsync(path, isFolder: true);
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void Start() =>
        _onStart(SelectedPath!, new EncodeOptions(SelectedEccLevel.Level, Compress));

    private bool CanStart() => SelectedPath is not null && SourceInfo is { IsValid: true } && !IsValidating;

    private async Task SelectAsync(string path, bool isFolder)
    {
        SelectedPath = path;
        SourceInfo = null;
        IsValidating = true;
        CompressLocked = isFolder;
        if (isFolder)
            Compress = true;

        try
        {
            SourceInfo = await _validator.ValidateAsync(path);
        }
        finally
        {
            IsValidating = false;
            UpdateDetails();
            OnPropertyChanged(nameof(SourceSummary));
            StartCommand.NotifyCanExecuteChanged();
        }
    }

    partial void OnSelectedEccLevelChanged(EccChoice value) => UpdateDetails();

    partial void OnCompressChanged(bool value) => UpdateDetails();

    private void UpdateDetails()
    {
        if (SelectedPath is null || SourceInfo is not { IsValid: true } info)
        {
            SourceDetails = "";
            EstimatedFrames = "";
        }
        else if (info.IsFolder)
        {
            var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(SelectedPath));
            SourceDetails = $"Folder “{name}” · {info.FileCount:N0} files · {ByteFormat.Bytes(info.TotalBytes)} · modified {FormatModified(SelectedPath)}";
            EstimatedFrames = $"≈ up to {FrameEstimate(info.TotalBytes)} frames (usually fewer after compression)";
        }
        else
        {
            var fi = new FileInfo(SelectedPath);
            var kind = string.IsNullOrEmpty(fi.Extension) ? "file" : fi.Extension.TrimStart('.').ToUpperInvariant() + " file";
            SourceDetails = $"“{fi.Name}” · {kind} · {ByteFormat.Bytes(info.TotalBytes)} · modified {fi.LastWriteTime:g}";
            EstimatedFrames = Compress
                ? $"≈ up to {FrameEstimate(info.TotalBytes)} frames (usually fewer after compression)"
                : $"{FrameEstimate(info.TotalBytes)} frames to display";
        }

        OnPropertyChanged(nameof(SourceDetails));
        OnPropertyChanged(nameof(EstimatedFrames));
    }

    private long FrameEstimate(long payloadBytes)
    {
        int perFrame = SelectedEccLevel.Level.PayloadBytesPerFrame();
        return (payloadBytes + perFrame - 1) / perFrame + 1;
    }

    private static string FormatModified(string folderPath)
    {
        try { return Directory.GetLastWriteTime(folderPath).ToString("g"); }
        catch { return "unknown"; }
    }
}
