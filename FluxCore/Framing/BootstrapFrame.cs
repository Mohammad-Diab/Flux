using FluxCore.Imaging;

namespace FluxCore.Framing;

/// <summary>
/// The fixed geometry and codec of the metadata frame (frame 0): a 96×54 grid encoded in
/// black/white at 1 bit per tile, protected by two interleaved RS(255,127) codewords. This is
/// the bootstrap anchor — the receiver knows nothing about the transfer yet, so every parameter
/// here is compile-time shared and never user-driven. Mono because frame 0 must be decodable on
/// the worst channel any payload tier targets (the rugged tier exists for chroma-destroying
/// links, so the frame that bootstraps it cannot itself depend on colour).
/// </summary>
public static class BootstrapFrame
{
    /// <summary>The fixed frame-0 layout: 96×54 tiles at 8 px, mono.</summary>
    public static FrameLayout Layout { get; } = FrameLayout.CreateBootstrap(96, 54, 8);

    /// <summary>Reed-Solomon codewords in the metadata frame, each RS(255,127).</summary>
    public const int CodewordCount = 2;

    /// <summary>Data bytes per metadata codeword (RS(255,127), maximum protection).</summary>
    public const int CodewordDataBytes = 127;

    /// <summary>Usable metadata content bytes (2 × 127).</summary>
    public const int ContentBytes = CodewordCount * CodewordDataBytes;

    /// <summary>Encoded bytes across all metadata codewords (2 × 255).</summary>
    public const int EncodedBytes = CodewordCount * FrameFormat.CodewordLength;

    /// <summary>Parity symbols per metadata codeword.</summary>
    public const int ParitySymbols = FrameFormat.CodewordLength - CodewordDataBytes;

    /// <summary>Metadata-frame tiles consumed at 1 bit per tile (510 × 8 = 4080); the rest render black.</summary>
    public const int TilesUsed = EncodedBytes * 8 / MonoColors.BitsPerTile;

    /// <summary>
    /// All header-role and data-role tiles in row-major scan order. Frame 0 carries no in-image
    /// FrameHeader, so the header region is repurposed as metadata capacity.
    /// </summary>
    public static IReadOnlyList<(int X, int Y)> MetadataFrameTiles { get; } = CollectMetadataFrameTiles();

    private static (int X, int Y)[] CollectMetadataFrameTiles()
    {
        var tiles = new List<(int X, int Y)>();
        for (int y = 0; y < Layout.GridHeightTiles; y++)
        {
            for (int x = 0; x < Layout.GridWidthTiles; x++)
            {
                var role = Layout.GetRole(x, y);
                if (role is TileRole.Header or TileRole.Data)
                    tiles.Add((x, y));
            }
        }

        if (tiles.Count < TilesUsed)
            throw new InvalidOperationException(
                $"The metadata frame needs {TilesUsed} tiles but only {tiles.Count} are available.");

        return tiles.ToArray();
    }
}
