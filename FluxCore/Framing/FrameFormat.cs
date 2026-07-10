using FluxCore.Imaging;

namespace FluxCore.Framing;

/// <summary>
/// Defines the fixed Flux frame format v2: a 160x90 tile grid with corner finder patterns,
/// timing patterns, three redundant header copies, a parity beacon, and stride-interleaved
/// Reed-Solomon data tiles. All geometry is compile-time fixed; this class is the single
/// source of truth for tile roles, scan order, and interleaving.
/// </summary>
public static class FrameFormat
{
    /// <summary>Frame format version encoded in every frame header.</summary>
    public const byte Version = 2;

    /// <summary>Grid width in tiles.</summary>
    public const int GridWidthTiles = 160;

    /// <summary>Grid height in tiles.</summary>
    public const int GridHeightTiles = 90;

    /// <summary>Total tiles in the grid.</summary>
    public const int TotalTiles = GridWidthTiles * GridHeightTiles;

    /// <summary>Tile edge length in pixels at canonical scale.</summary>
    public const int TilePixelSize = 8;

    /// <summary>White quiet zone around the grid in pixels at canonical scale.</summary>
    public const int QuietZonePx = 2 * TilePixelSize;

    /// <summary>Canonical rendered frame width in pixels.</summary>
    public const int FrameWidthPx = GridWidthTiles * TilePixelSize + 2 * QuietZonePx;

    /// <summary>Canonical rendered frame height in pixels.</summary>
    public const int FrameHeightPx = GridHeightTiles * TilePixelSize + 2 * QuietZonePx;

    /// <summary>Number of Reed-Solomon codewords per frame at the default 8-bit colour depth.</summary>
    public const int CodewordCount = 53;

    /// <summary>Symbols per Reed-Solomon codeword (GF(256) maximum).</summary>
    public const int CodewordLength = 255;

    /// <summary>Number of tiles carrying codeword symbols.</summary>
    public const int DataTileCount = CodewordCount * CodewordLength;

    /// <summary>Number of redundant header copies per frame.</summary>
    public const int HeaderCopyCount = 3;

    /// <summary>Tiles (symbols) per header copy: RS(48,16).</summary>
    public const int HeaderCopyLength = 48;

    /// <summary>Edge length of the square 7x7 finder pattern in tiles.</summary>
    public const int FinderSizeTiles = 7;

    /// <summary>Edge length of a reserved corner block (finder plus quiet ring) in tiles.</summary>
    public const int CornerBlockSizeTiles = 8;

    /// <summary>Reed-Solomon codewords in the metadata frame (frame 0), each RS(255,127).</summary>
    public const int MetadataCodewordCount = 12;

    /// <summary>Data bytes per metadata-frame codeword (RS(255,127), maximum protection).</summary>
    public const int MetadataCodewordDataBytes = 127;

    /// <summary>Usable payload bytes in the metadata frame (12 x 127).</summary>
    public const int MetadataContentBytes = MetadataCodewordCount * MetadataCodewordDataBytes;

    /// <summary>Encoded bytes across all metadata codewords (12 x 255).</summary>
    public const int MetadataEncodedBytes = MetadataCodewordCount * CodewordLength;

    /// <summary>Parity symbols per metadata-frame codeword.</summary>
    public const int MetadataParitySymbols = CodewordLength - MetadataCodewordDataBytes;

    /// <summary>
    /// Metadata-frame tiles consumed at 3 bits per tile to carry the encoded codewords
    /// (12 x 255 x 8 / 3 = 8160). Remaining metadata-frame tiles render black.
    /// </summary>
    public const int MetadataTilesUsed = MetadataEncodedBytes * 8 / CubeCornerColors.BitsPerTile;

    private static readonly TileRole[] Roles = new TileRole[TotalTiles];
    private static readonly (int X, int Y)[] DataTilePositions;
    private static readonly (int X, int Y)[] PadTilePositions;
    private static readonly (int X, int Y)[][] HeaderCopyPositions;
    private static readonly (int X, int Y)[] BeaconPositions;
    private static readonly (int X, int Y)[] MetadataFrameTilePositions;

    /// <summary>
    /// Finder pattern centers in tile coordinates, order: top-left, top-right, bottom-left, bottom-right.
    /// </summary>
    public static readonly IReadOnlyList<(double X, double Y)> FinderCentersTiles =
    [
        (3.5, 3.5),
        (156.5, 3.5),
        (3.5, 86.5),
        (156.5, 86.5),
    ];

    static FrameFormat()
    {
        Array.Fill(Roles, TileRole.Data);

        MarkCornerBlocks();
        MarkTimingPattern();
        HeaderCopyPositions = MarkHeaderCopies();
        BeaconPositions = MarkBeacon();
        (DataTilePositions, PadTilePositions) = AssignDataAndPadTiles();
        MetadataFrameTilePositions = CollectMetadataFrameTiles();
    }

    /// <summary>Gets the role of the tile at the given grid coordinates.</summary>
    /// <param name="x">Tile column (0-159).</param>
    /// <param name="y">Tile row (0-89).</param>
    public static TileRole GetRole(int x, int y)
    {
        ValidateCoordinates(x, y);
        return Roles[y * GridWidthTiles + x];
    }

    /// <summary>
    /// Positions of the tiles carrying codeword symbols, in scan order (row-major, skipping reserved tiles).
    /// </summary>
    public static IReadOnlyList<(int X, int Y)> DataTiles => DataTilePositions;

    /// <summary>Positions of trailing unused tiles, always rendered white.</summary>
    public static IReadOnlyList<(int X, int Y)> PadTiles => PadTilePositions;

    /// <summary>Positions of the 4x4 beacon block tiles.</summary>
    public static IReadOnlyList<(int X, int Y)> BeaconTiles => BeaconPositions;

    /// <summary>
    /// All header-role and data-role tiles in row-major scan order. On the metadata frame these
    /// carry cube-corner colors (3 bits each) rather than palette symbols; the header region is
    /// repurposed as extra metadata capacity since frame 0 carries no in-image FrameHeader.
    /// </summary>
    public static IReadOnlyList<(int X, int Y)> MetadataFrameTiles => MetadataFrameTilePositions;

    /// <summary>Gets the tile positions of one header copy, in symbol order.</summary>
    /// <param name="copyIndex">Header copy index (0-2).</param>
    public static IReadOnlyList<(int X, int Y)> GetHeaderCopyTiles(int copyIndex)
    {
        if (copyIndex < 0 || copyIndex >= HeaderCopyCount)
            throw new ArgumentOutOfRangeException(nameof(copyIndex));
        return HeaderCopyPositions[copyIndex];
    }

    /// <summary>
    /// Maps a data tile index (position in scan order) to its codeword and symbol indices.
    /// Stride-53 interleaving: any 53 consecutive data tiles touch 53 distinct codewords,
    /// so a contiguous burst of damage spreads evenly across all codewords.
    /// </summary>
    /// <param name="dataTileIndex">Index into <see cref="DataTiles"/> (0 to 13514).</param>
    public static (int Codeword, int Symbol) ToCodewordSymbol(int dataTileIndex)
    {
        if (dataTileIndex < 0 || dataTileIndex >= DataTileCount)
            throw new ArgumentOutOfRangeException(nameof(dataTileIndex));
        return (dataTileIndex % CodewordCount, dataTileIndex / CodewordCount);
    }

    /// <summary>
    /// Number of RS(255,k) codewords a payload frame carries at a given colour depth. Fewer bits
    /// per tile fit fewer whole codewords into the fixed data-tile budget (8 bits → 53). The tiles
    /// left over past the packed codewords render as pad.
    /// </summary>
    /// <param name="bitsPerTile">Colour depth in bits per tile (1–8).</param>
    public static int CodewordsForBits(int bitsPerTile)
    {
        if (bitsPerTile is < 1 or > 8)
            throw new ArgumentOutOfRangeException(nameof(bitsPerTile));
        return DataTileCount * bitsPerTile / (CodewordLength * 8);
    }

    /// <summary>Maps a codeword and symbol index back to the data tile index in scan order.</summary>
    /// <param name="codeword">Codeword index (0-52).</param>
    /// <param name="symbol">Symbol index within the codeword (0-254).</param>
    public static int ToDataTileIndex(int codeword, int symbol)
    {
        if (codeword < 0 || codeword >= CodewordCount)
            throw new ArgumentOutOfRangeException(nameof(codeword));
        if (symbol < 0 || symbol >= CodewordLength)
            throw new ArgumentOutOfRangeException(nameof(symbol));
        return symbol * CodewordCount + codeword;
    }

    /// <summary>
    /// Determines whether a structural tile (finder or timing) is black. White quiet-ring
    /// tiles inside corner blocks return false. Must not be called for other roles.
    /// </summary>
    /// <param name="x">Tile column (0-159).</param>
    /// <param name="y">Tile row (0-89).</param>
    public static bool IsStructuralBlack(int x, int y)
    {
        var role = GetRole(x, y);
        return role switch
        {
            TileRole.Finder => IsFinderBlack(x, y),
            TileRole.Timing => IsTimingBlack(x, y),
            _ => throw new ArgumentException($"Tile ({x},{y}) has role {role}, not a structural tile."),
        };
    }

    /// <summary>Converts a tile-space coordinate to canonical pixel coordinates.</summary>
    /// <param name="tileX">Tile-space X (fractional values address within-tile positions).</param>
    /// <param name="tileY">Tile-space Y (fractional values address within-tile positions).</param>
    public static (double X, double Y) TileToPixel(double tileX, double tileY) =>
        (QuietZonePx + tileX * TilePixelSize, QuietZonePx + tileY * TilePixelSize);

    private static void MarkCornerBlocks()
    {
        foreach (var (blockX, blockY) in CornerBlockOrigins())
        {
            for (int y = blockY; y < blockY + CornerBlockSizeTiles; y++)
            {
                for (int x = blockX; x < blockX + CornerBlockSizeTiles; x++)
                {
                    Roles[y * GridWidthTiles + x] = TileRole.Finder;
                }
            }
        }
    }

    private static void MarkTimingPattern()
    {
        for (int x = CornerBlockSizeTiles; x < GridWidthTiles - CornerBlockSizeTiles; x++)
        {
            Roles[x] = TileRole.Timing;
        }

        for (int y = CornerBlockSizeTiles; y < GridHeightTiles - CornerBlockSizeTiles; y++)
        {
            Roles[y * GridWidthTiles] = TileRole.Timing;
        }
    }

    private static (int X, int Y)[][] MarkHeaderCopies()
    {
        var copies = new (int X, int Y)[HeaderCopyCount][];
        copies[0] = MarkHeaderRun(startX: 8, startY: 1, stepX: 1, stepY: 0);
        copies[1] = MarkHeaderRun(startX: 159, startY: 8, stepX: 0, stepY: 1);
        copies[2] = MarkHeaderRun(startX: 104, startY: 88, stepX: 1, stepY: 0);
        return copies;
    }

    private static (int X, int Y)[] MarkHeaderRun(int startX, int startY, int stepX, int stepY)
    {
        var tiles = new (int X, int Y)[HeaderCopyLength];
        for (int i = 0; i < HeaderCopyLength; i++)
        {
            int x = startX + i * stepX;
            int y = startY + i * stepY;
            Roles[y * GridWidthTiles + x] = TileRole.Header;
            tiles[i] = (x, y);
        }

        return tiles;
    }

    private static (int X, int Y)[] MarkBeacon()
    {
        var tiles = new (int X, int Y)[16];
        int i = 0;
        for (int y = 2; y <= 5; y++)
        {
            for (int x = 78; x <= 81; x++)
            {
                Roles[y * GridWidthTiles + x] = TileRole.Beacon;
                tiles[i++] = (x, y);
            }
        }

        return tiles;
    }

    private static ((int X, int Y)[] Data, (int X, int Y)[] Pad) AssignDataAndPadTiles()
    {
        var data = new List<(int X, int Y)>(DataTileCount);
        var pad = new List<(int X, int Y)>();

        for (int y = 0; y < GridHeightTiles; y++)
        {
            for (int x = 0; x < GridWidthTiles; x++)
            {
                if (Roles[y * GridWidthTiles + x] != TileRole.Data)
                    continue;

                if (data.Count < DataTileCount)
                {
                    data.Add((x, y));
                }
                else
                {
                    Roles[y * GridWidthTiles + x] = TileRole.Pad;
                    pad.Add((x, y));
                }
            }
        }

        if (data.Count != DataTileCount)
            throw new InvalidOperationException(
                $"Frame format geometry is inconsistent: {data.Count} data tiles, expected {DataTileCount}.");

        return (data.ToArray(), pad.ToArray());
    }

    private static (int X, int Y)[] CollectMetadataFrameTiles()
    {
        var tiles = new List<(int X, int Y)>(HeaderCopyCount * HeaderCopyLength + DataTileCount);
        for (int y = 0; y < GridHeightTiles; y++)
        {
            for (int x = 0; x < GridWidthTiles; x++)
            {
                var role = Roles[y * GridWidthTiles + x];
                if (role is TileRole.Header or TileRole.Data)
                    tiles.Add((x, y));
            }
        }

        if (tiles.Count < MetadataTilesUsed)
            throw new InvalidOperationException(
                $"Metadata frame needs {MetadataTilesUsed} tiles but only {tiles.Count} are available.");

        return tiles.ToArray();
    }

    private static IEnumerable<(int X, int Y)> CornerBlockOrigins()
    {
        yield return (0, 0);
        yield return (GridWidthTiles - CornerBlockSizeTiles, 0);
        yield return (0, GridHeightTiles - CornerBlockSizeTiles);
        yield return (GridWidthTiles - CornerBlockSizeTiles, GridHeightTiles - CornerBlockSizeTiles);
    }

    private static bool IsFinderBlack(int x, int y)
    {
        foreach (var (blockX, blockY) in CornerBlockOrigins())
        {
            if (x < blockX || x >= blockX + CornerBlockSizeTiles ||
                y < blockY || y >= blockY + CornerBlockSizeTiles)
                continue;

            int finderX = blockX == 0 ? blockX : blockX + 1;
            int finderY = blockY == 0 ? blockY : blockY + 1;
            int localX = x - finderX;
            int localY = y - finderY;

            if (localX < 0 || localX >= FinderSizeTiles || localY < 0 || localY >= FinderSizeTiles)
                return false;

            bool onBorder = localX == 0 || localX == FinderSizeTiles - 1 ||
                            localY == 0 || localY == FinderSizeTiles - 1;
            bool inCenter = localX >= 2 && localX <= 4 && localY >= 2 && localY <= 4;
            return onBorder || inCenter;
        }

        throw new ArgumentException($"Tile ({x},{y}) is not inside a corner block.");
    }

    private static bool IsTimingBlack(int x, int y) => y == 0 ? x % 2 == 0 : y % 2 == 0;

    private static void ValidateCoordinates(int x, int y)
    {
        if (x < 0 || x >= GridWidthTiles)
            throw new ArgumentOutOfRangeException(nameof(x));
        if (y < 0 || y >= GridHeightTiles)
            throw new ArgumentOutOfRangeException(nameof(y));
    }
}
