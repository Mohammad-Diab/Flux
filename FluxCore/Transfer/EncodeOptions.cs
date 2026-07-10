using FluxCore.Ecc;
using FluxCore.Framing;

namespace FluxCore.Transfer;

/// <summary>
/// Options controlling how a source is encoded into frames.
/// </summary>
/// <param name="EccLevel">Error-correction level for payload frames (frame 0 always uses Max).</param>
/// <param name="Compress">Whether to 7z-compress the source. Folders are always compressed.</param>
/// <param name="GridWidthTiles">Payload-frame grid width in tiles (frame 0 is always the default grid).</param>
/// <param name="GridHeightTiles">Payload-frame grid height in tiles.</param>
/// <param name="TilePixelSize">Payload-frame tile edge length in pixels.</param>
public sealed record EncodeOptions(
    EccLevel EccLevel = EccLevel.Medium,
    bool Compress = true,
    int GridWidthTiles = FrameFormat.GridWidthTiles,
    int GridHeightTiles = FrameFormat.GridHeightTiles,
    int TilePixelSize = FrameFormat.TilePixelSize);
