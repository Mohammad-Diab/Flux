using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Flux.Ui;
using Flux.Ui.Services;
using FluxCast.Services;
using FluxCore.Ecc;
using FluxCore.Framing;
using FluxCore.Imaging;
using FluxCore.Transfer;

namespace FluxCast.ViewModels;

/// <summary>An ECC level choice presented in the setup screen.</summary>
public sealed record EccChoice(EccLevel Level, string Label)
{
    public override string ToString() => Label;
}

/// <summary>A tile-size choice; smaller tiles fit more per screen (faster) but need a cleaner channel.</summary>
public sealed record TileSizeChoice(int TilePx, string Label)
{
    public override string ToString() => Label;
}

/// <summary>A colour-count choice; more colours carry more per tile (faster) but need a cleaner channel.</summary>
public sealed record ColorChoice(int ColorCount, string Label)
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
    private readonly Action<EncodeOptions> _onTest;

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
    private TileSizeChoice _selectedTileSize;

    [ObservableProperty]
    private ColorChoice _selectedColor;

    [ObservableProperty]
    private bool _compress = true;

    [ObservableProperty]
    private bool _compressLocked;

    private readonly int _displayWidthPx;
    private readonly int _displayHeightPx;
    private FrameLayout _layout;
    private int _bitsPerTile = 8;

    /// <summary>Gets the selectable ECC levels.</summary>
    public IReadOnlyList<EccChoice> EccLevels { get; } =
    [
        new(EccLevel.Low, "Low — fastest, clean captures only"),
        new(EccLevel.Medium, "Medium — recommended"),
        new(EccLevel.High, "High — for lossy channels"),
        new(EccLevel.Max, "Max — for the worst channels"),
    ];

    /// <summary>Gets the selectable tile sizes; the grid is auto-fitted to the screen at the chosen size.</summary>
    public IReadOnlyList<TileSizeChoice> TileSizes { get; } =
    [
        new(12, "Large tiles — most robust, fewer per frame"),
        new(10, "Medium tiles"),
        new(8, "Standard tiles — recommended"),
        new(6, "Small tiles — fastest, clean channel only"),
    ];

    /// <summary>Gets the selectable colour counts; more colours pack more bits per tile.</summary>
    public IReadOnlyList<ColorChoice> Colors { get; } =
    [
        new(256, "256 colours — standard, any channel"),
        new(512, "512 colours — +12.5%, clean channel"),
        new(1024, "1024 colours — +25%, pixel-perfect only"),
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

    /// <summary>Gets the fitted grid, capacity, and throughput readout for the chosen tile/ECC settings.</summary>
    public string GridSummary { get; private set; } = "";

    /// <summary>Gets the clear-channel caution shown when the tiles are small enough to be fragile.</summary>
    public string GridCaution { get; private set; } = "";

    public EncodeSetupViewModel(
        SourceValidator validator,
        DialogService dialogs,
        Action<string, EncodeOptions> onStart,
        Action<EncodeOptions> onTest,
        (int Width, int Height) displayPixels)
    {
        _validator = validator;
        _dialogs = dialogs;
        _onStart = onStart;
        _onTest = onTest;
        _displayWidthPx = displayPixels.Width;
        _displayHeightPx = displayPixels.Height;
        _selectedEccLevel = EccLevels[1];
        _selectedTileSize = TileSizes[2];
        _selectedColor = Colors[0];
        _layout = FrameLayout.Default;
        RecomputeLayout();
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

    /// <summary>Accepts a dropped path, detecting file vs folder from disk.</summary>
    public Task SelectDroppedAsync(string path)
    {
        if (Directory.Exists(path))
            return SelectAsync(path, isFolder: true);
        if (File.Exists(path))
            return SelectAsync(path, isFolder: false);
        return Task.CompletedTask;
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void Start() => _onStart(SelectedPath!, CurrentOptions());

    /// <summary>Renders a throwaway frame at the current settings to check the channel — needs no source.</summary>
    [RelayCommand]
    private void TestFrame() => _onTest(CurrentOptions());

    private EncodeOptions CurrentOptions() => new(
        SelectedEccLevel.Level, Compress, _layout.GridWidthTiles, _layout.GridHeightTiles,
        _layout.TilePixelSize, SelectedColor.ColorCount);

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

    partial void OnSelectedEccLevelChanged(EccChoice value) => RecomputeLayout();

    partial void OnSelectedTileSizeChanged(TileSizeChoice value) => RecomputeLayout();

    partial void OnSelectedColorChanged(ColorChoice value) => RecomputeLayout();

    partial void OnCompressChanged(bool value) => UpdateDetails();

    private void RecomputeLayout()
    {
        _bitsPerTile = PaletteGenerator.BitsForCount(SelectedColor.ColorCount);
        // Cap the grid so a frame's payload fits the ushort length field at the chosen depth.
        int dataBytes = SelectedEccLevel.Level.DataBytesPerCodeword();
        int maxCodewords = (int)((long)ushort.MaxValue * 8 / ((long)dataBytes * _bitsPerTile));
        _layout = FrameLayout.FitToDisplay(_displayWidthPx, _displayHeightPx, SelectedTileSize.TilePx, maxCodewords);

        int codewords = _layout.CodewordsForBits(_bitsPerTile);
        int bytesPerFrame = SelectedEccLevel.Level.PayloadBytesPerFrame(codewords);
        double throughput = (double)codewords / FrameFormat.CodewordCount;
        string colours = SelectedColor.ColorCount == 256 ? "" : $"{SelectedColor.ColorCount} colours · ";
        GridSummary =
            $"{_layout.GridWidthTiles}×{_layout.GridHeightTiles} tiles · {colours}{ByteFormat.Bytes(bytesPerFrame)}/frame · ≈{throughput:0.0}× throughput";

        GridCaution = BuildCaution();

        OnPropertyChanged(nameof(GridSummary));
        OnPropertyChanged(nameof(GridCaution));
        UpdateDetails();
    }

    private string BuildCaution() => SelectedColor.ColorCount switch
    {
        1024 => "1024 colours decodes only on a near-pixel-perfect channel (local capture or exact PNG folders) — it will fail over RDP or any compression.",
        512 => "512 colours needs a clean channel; run a test frame before committing to a long transfer.",
        _ => SelectedTileSize.TilePx <= 6
            ? "Small tiles — use only on a clean, near-pixel-perfect channel (local capture or exact PNGs)."
            : "",
    };

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
        int perFrame = SelectedEccLevel.Level.PayloadBytesPerFrame(_layout.CodewordsForBits(_bitsPerTile));
        return (payloadBytes + perFrame - 1) / perFrame + 1;
    }

    private static string FormatModified(string folderPath)
    {
        try { return Directory.GetLastWriteTime(folderPath).ToString("g"); }
        catch { return "unknown"; }
    }
}
