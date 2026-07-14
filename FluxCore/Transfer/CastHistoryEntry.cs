using FluxCore.Ecc;
using FluxCore.Imaging;

namespace FluxCore.Transfer;

/// <summary>A past cast (one render variant of a payload) surfaced in FluxCast's history list.</summary>
/// <param name="SessionDirectory">The render-variant folder (delete/present target).</param>
/// <param name="FramesDirectory">Folder holding the frame PNGs.</param>
/// <param name="DisplayName">Source file or folder name.</param>
/// <param name="SourcePath">Original source path, if known (it may no longer exist on disk).</param>
/// <param name="SourceKind">Whether the source was a file or a folder.</param>
/// <param name="TotalFrames">Total frames including frame 0.</param>
/// <param name="PayloadLength">Transfer payload size in bytes.</param>
/// <param name="CreatedUtc">When this variant was first encoded.</param>
/// <param name="IsComplete">Whether every frame is present on disk (so it can be re-presented as-is).</param>
/// <param name="EccLevel">ECC level of this render variant.</param>
/// <param name="GridWidthTiles">Payload-frame grid width in tiles.</param>
/// <param name="GridHeightTiles">Payload-frame grid height in tiles.</param>
/// <param name="ColorCount">Data-tile colour count of this render variant.</param>
/// <param name="PaletteKind">Data-tile palette family of this render variant.</param>
public sealed record CastHistoryEntry(
    string SessionDirectory,
    string FramesDirectory,
    string DisplayName,
    string? SourcePath,
    SourceKind SourceKind,
    uint TotalFrames,
    long PayloadLength,
    DateTimeOffset CreatedUtc,
    bool IsComplete,
    EccLevel EccLevel,
    int GridWidthTiles,
    int GridHeightTiles,
    int ColorCount,
    PaletteKind PaletteKind = PaletteKind.Standard);
