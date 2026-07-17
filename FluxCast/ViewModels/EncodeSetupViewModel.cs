using System.IO;
using System.Linq;
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

/// <summary>A palette choice; more colours carry more per tile (faster), or the rugged grayscale tier for lossy channels.</summary>
public sealed record ColorChoice(int ColorCount, string Label, PaletteKind Kind = PaletteKind.Standard)
{
    public override string ToString() => Label;
}

/// <summary>The setup screen's preset/customization level. Default and Rugged hide the levers; Advanced reveals them.</summary>
public enum SetupMode
{
    /// <summary>Balanced recommended settings (Medium ECC, Standard tiles, 256 colours), levers hidden.</summary>
    Default,

    /// <summary>Robust preset for RDP/lossy channels (High ECC, Large tiles, grayscale-8), levers hidden.</summary>
    Rugged,

    /// <summary>Full manual control — ECC, tile size, palette, and compression are editable.</summary>
    Advanced,
}

/// <summary>How safe the current tile-size/colour pairing is for the fitted tile pixel size.</summary>
public enum SetupWarningLevel
{
    /// <summary>Tiles are large enough for the chosen palette; no caution.</summary>
    None,

    /// <summary>Decodable only on a near-pixel-perfect channel (amber).</summary>
    Caution,

    /// <summary>Tiles too small for the palette — decoding will fail (red).</summary>
    Severe,
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
    private SetupMode _mode = SetupMode.Default;

    [ObservableProperty]
    private bool _compress = true;

    [ObservableProperty]
    private bool _compressLocked;

    private int _displayWidthPx;
    private int _displayHeightPx;
    private FrameLayout _layout;
    private int _bitsPerTile = 8;
    private bool _recomputing;

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
        new(PaletteGenerator.RuggedColorCount, "Rugged grayscale-8 — survives RDP / lossy", PaletteKind.Rugged),
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

    /// <summary>Gets the capture-robustness caution for the fitted tile size and palette (empty when safe).</summary>
    public string GridCaution { get; private set; } = "";

    /// <summary>Gets how severe <see cref="GridCaution"/> is, so the view can colour it amber or red.</summary>
    public SetupWarningLevel WarningLevel { get; private set; } = SetupWarningLevel.None;

    /// <summary>Gets the colours selectable at the fitted tile size; ones too small to decode are dropped.</summary>
    public IReadOnlyList<ColorChoice> AvailableColors { get; private set; }

    /// <summary>Gets whether the customization levers (ECC, tile size, palette, compress) are shown.</summary>
    public bool IsAdvanced => Mode == SetupMode.Advanced;

    /// <summary>Selects Default mode; bound to the Default segment.</summary>
    public bool IsDefaultMode
    {
        get => Mode == SetupMode.Default;
        set { if (value) Mode = SetupMode.Default; }
    }

    /// <summary>Selects Rugged mode; bound to the Rugged segment.</summary>
    public bool IsRuggedMode
    {
        get => Mode == SetupMode.Rugged;
        set { if (value) Mode = SetupMode.Rugged; }
    }

    /// <summary>Selects Advanced mode; bound to the Advanced segment.</summary>
    public bool IsAdvancedMode
    {
        get => Mode == SetupMode.Advanced;
        set { if (value) Mode = SetupMode.Advanced; }
    }

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
        _selectedColor = Colors.First(c => c.Kind == PaletteKind.Standard && c.ColorCount == 256);
        _layout = FrameLayout.Default;
        AvailableColors = Colors;
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

    /// <summary>Sets the presenter canvas the grid is fitted to (physical px), then refits.</summary>
    public void SetDisplayCanvas(int width, int height)
    {
        _displayWidthPx = width;
        _displayHeightPx = height;
        RecomputeLayout();
    }

    // Default and Rugged are complete presets; Advanced reads the selectors. Every derived value routes through these.
    private EccLevel EffectiveEcc => Mode switch
    {
        SetupMode.Default => EccLevel.Medium,
        SetupMode.Rugged => EccLevel.High,
        _ => SelectedEccLevel.Level,
    };

    private int EffectiveTilePx => Mode switch
    {
        SetupMode.Default => 8,
        SetupMode.Rugged => 12,
        _ => SelectedTileSize.TilePx,
    };

    private int EffectiveColorCount => Mode switch
    {
        SetupMode.Default => 256,
        SetupMode.Rugged => PaletteGenerator.RuggedColorCount,
        _ => SelectedColor.ColorCount,
    };

    private PaletteKind EffectivePaletteKind => Mode switch
    {
        SetupMode.Rugged => PaletteKind.Rugged,
        SetupMode.Advanced => SelectedColor.Kind,
        _ => PaletteKind.Standard,
    };

    // Presets always compress; only Advanced honours the checkbox.
    private bool EffectiveCompress => Mode == SetupMode.Advanced ? Compress : true;

    private EncodeOptions CurrentOptions() => new(
        EffectiveEcc, EffectiveCompress, _layout.GridWidthTiles, _layout.GridHeightTiles,
        _layout.TilePixelSize, EffectiveColorCount, EffectivePaletteKind);

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

    partial void OnModeChanged(SetupMode value)
    {
        OnPropertyChanged(nameof(IsAdvanced));
        OnPropertyChanged(nameof(IsDefaultMode));
        OnPropertyChanged(nameof(IsRuggedMode));
        OnPropertyChanged(nameof(IsAdvancedMode));
        RecomputeLayout();
    }

    partial void OnCompressChanged(bool value) => UpdateDetails();

    private void RecomputeLayout()
    {
        if (_recomputing)
            return;
        _recomputing = true;
        try
        {
            RefreshAvailableColors();

            _bitsPerTile = PaletteGenerator.BitsForCount(EffectiveColorCount);
            // Per-frame payload is a uint, so the grid is bounded only by the display (no codeword cap).
            _layout = FrameLayout.FitToDisplay(
                _displayWidthPx, _displayHeightPx, EffectiveTilePx, maxCodewords: 0, bitsPerTile: _bitsPerTile);

            int codewords = _layout.CodewordsForBits(_bitsPerTile);
            int bytesPerFrame = EffectiveEcc.PayloadBytesPerFrame(codewords);
            double throughput = (double)codewords / FrameFormat.CodewordCount;
            if (EffectivePaletteKind == PaletteKind.Rugged)
                GridSummary =
                    $"{_layout.GridWidthTiles}×{_layout.GridHeightTiles} tiles · rugged 8-gray · {ByteFormat.Bytes(bytesPerFrame)}/frame · survives chroma-lossy / RDP links";
            else
            {
                string colours = EffectiveColorCount == 256 ? "" : $"{EffectiveColorCount} colours · ";
                GridSummary =
                    $"{_layout.GridWidthTiles}×{_layout.GridHeightTiles} tiles · {colours}{ByteFormat.Bytes(bytesPerFrame)}/frame · ≈{throughput:0.0}× throughput";
            }

            BuildCaution();

            OnPropertyChanged(nameof(GridSummary));
            OnPropertyChanged(nameof(GridCaution));
            OnPropertyChanged(nameof(WarningLevel));
            UpdateDetails();
        }
        finally
        {
            _recomputing = false;
        }
    }

    // Offer only colours the fitted tile size can carry robustly (safe floor); snap off a hidden one.
    private void RefreshAvailableColors()
    {
        int tilePx = EffectiveTilePx;
        AvailableColors = Colors
            .Where(c => tilePx >= PaletteGenerator.CaptureTilePxFloor(c.ColorCount, c.Kind).Safe)
            .ToList();
        OnPropertyChanged(nameof(AvailableColors));

        if (IsAdvanced && !AvailableColors.Contains(SelectedColor))
            SelectedColor = AvailableColors.LastOrDefault(c => c.Kind == PaletteKind.Standard) ?? AvailableColors[^1];
    }

    // Tiles are always large enough (safe filter), so the only caution left is the low colour-distance
    // of 512/1024 — clean-channel tiers regardless of tile size.
    private void BuildCaution()
    {
        WarningLevel = EffectivePaletteKind == PaletteKind.Standard && EffectiveColorCount >= 512
            ? SetupWarningLevel.Caution
            : SetupWarningLevel.None;
        GridCaution = EffectiveColorCount switch
        {
            _ when EffectivePaletteKind == PaletteKind.Rugged => "",
            1024 => "1024 colours decodes only on a near-pixel-perfect channel (local capture or exact PNGs) — it fails over RDP or compression.",
            512 => "512 colours needs a clean channel — run Test frame before a long transfer.",
            _ => "",
        };
    }

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
            EstimatedFrames = EffectiveCompress
                ? $"≈ up to {FrameEstimate(info.TotalBytes)} frames (usually fewer after compression)"
                : $"{FrameEstimate(info.TotalBytes)} frames to display";
        }

        OnPropertyChanged(nameof(SourceDetails));
        OnPropertyChanged(nameof(EstimatedFrames));
    }

    private long FrameEstimate(long payloadBytes)
    {
        int perFrame = EffectiveEcc.PayloadBytesPerFrame(_layout.CodewordsForBits(_bitsPerTile));
        return (payloadBytes + perFrame - 1) / perFrame + 1;
    }

    private static string FormatModified(string folderPath)
    {
        try { return Directory.GetLastWriteTime(folderPath).ToString("g"); }
        catch { return "unknown"; }
    }
}
