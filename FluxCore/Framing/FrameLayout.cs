namespace FluxCore.Framing;

/// <summary>
/// The tile-role map, positions, and codeword layout for a frame grid of a given size — the
/// parametric form of the geometry that <see cref="FrameFormat"/> pins at 160×90. Corner finders,
/// timing patterns, the three header copies, and the beacon scale with the grid; the remaining
/// tiles carry stride-interleaved Reed–Solomon codewords. <see cref="Default"/> reproduces the
/// fixed <see cref="FrameFormat"/> layout byte-for-byte.
/// </summary>
public sealed class FrameLayout
{
    private readonly TileRole[] _roles;

    /// <summary>The canonical 160×90 @ 8-px layout — identical to <see cref="FrameFormat"/>.</summary>
    public static FrameLayout Default { get; } =
        new(FrameFormat.GridWidthTiles, FrameFormat.GridHeightTiles, FrameFormat.TilePixelSize);

    /// <summary>Grid width in tiles.</summary>
    public int GridWidthTiles { get; }

    /// <summary>Grid height in tiles.</summary>
    public int GridHeightTiles { get; }

    /// <summary>Tile edge length in pixels at canonical scale.</summary>
    public int TilePixelSize { get; }

    /// <summary>Total tiles in the grid.</summary>
    public int TotalTiles => GridWidthTiles * GridHeightTiles;

    /// <summary>White quiet zone around the grid in pixels.</summary>
    public int QuietZonePx => 2 * TilePixelSize;

    /// <summary>Rendered frame width in pixels.</summary>
    public int FrameWidthPx => GridWidthTiles * TilePixelSize + 2 * QuietZonePx;

    /// <summary>Rendered frame height in pixels.</summary>
    public int FrameHeightPx => GridHeightTiles * TilePixelSize + 2 * QuietZonePx;

    /// <summary>Number of Reed–Solomon codewords this grid carries at the default 8-bit depth.</summary>
    public int CodewordCount { get; }

    /// <summary>Tiles carrying codeword symbols (codeword count × 255).</summary>
    public int DataTileCount => CodewordCount * FrameFormat.CodewordLength;

    /// <summary>Data-tile positions in scan order (row-major, skipping reserved tiles).</summary>
    public IReadOnlyList<(int X, int Y)> DataTiles { get; }

    /// <summary>Trailing unused tile positions, always rendered white.</summary>
    public IReadOnlyList<(int X, int Y)> PadTiles { get; }

    /// <summary>The 4×4 beacon block tiles.</summary>
    public IReadOnlyList<(int X, int Y)> BeaconTiles { get; }

    /// <summary>Finder-pattern centres in tile coordinates: top-left, top-right, bottom-left, bottom-right.</summary>
    public IReadOnlyList<(double X, double Y)> FinderCentersTiles { get; }

    private readonly (int X, int Y)[][] _headerCopies;

    /// <summary>Creates the layout for a grid of the given size.</summary>
    /// <param name="gridWidthTiles">Grid width in tiles.</param>
    /// <param name="gridHeightTiles">Grid height in tiles.</param>
    /// <param name="tilePixelSize">Tile edge length in pixels.</param>
    public FrameLayout(int gridWidthTiles, int gridHeightTiles, int tilePixelSize)
    {
        int minEdge = 2 * FrameFormat.CornerBlockSizeTiles + FrameFormat.HeaderCopyLength;
        if (gridWidthTiles < minEdge || gridHeightTiles < minEdge)
            throw new ArgumentException(
                $"Grid must be at least {minEdge}×{minEdge} tiles to fit finders, timing, and header runs.");
        if (tilePixelSize < 1)
            throw new ArgumentOutOfRangeException(nameof(tilePixelSize));

        GridWidthTiles = gridWidthTiles;
        GridHeightTiles = gridHeightTiles;
        TilePixelSize = tilePixelSize;

        _roles = new TileRole[TotalTiles];
        Array.Fill(_roles, TileRole.Data);

        MarkCornerBlocks();
        MarkTimingPattern();
        _headerCopies = MarkHeaderCopies();
        BeaconTiles = MarkBeacon();

        int reserved = CountNonData();
        CodewordCount = (TotalTiles - reserved) / FrameFormat.CodewordLength;

        (DataTiles, PadTiles) = AssignDataAndPadTiles(DataTileCount);

        int w = GridWidthTiles, h = GridHeightTiles;
        FinderCentersTiles = [(3.5, 3.5), (w - 3.5, 3.5), (3.5, h - 3.5), (w - 3.5, h - 3.5)];
    }

    /// <summary>
    /// Derives the largest grid that fills a display of the given pixel size at the given tile
    /// size, preserving the display's aspect ratio (square tiles, so the rendered frame fills the
    /// screen without stretch or letterboxing). The result is clamped to the minimum decodable grid
    /// and, if <paramref name="maxCodewords"/> is positive, shrunk (keeping the aspect ratio) until
    /// its codeword count fits — the caller derives that budget from the per-frame byte cap and ECC.
    /// </summary>
    /// <param name="displayWidthPx">Usable display width in physical pixels.</param>
    /// <param name="displayHeightPx">Usable display height in physical pixels.</param>
    /// <param name="tilePixelSize">Tile edge length in pixels.</param>
    /// <param name="maxCodewords">Upper bound on codewords per frame, or 0 for no cap.</param>
    public static FrameLayout FitToDisplay(int displayWidthPx, int displayHeightPx, int tilePixelSize, int maxCodewords = 0)
    {
        if (tilePixelSize < 1)
            throw new ArgumentOutOfRangeException(nameof(tilePixelSize));

        int minEdge = 2 * FrameFormat.CornerBlockSizeTiles + FrameFormat.HeaderCopyLength;
        // FrameWidthPx = (grid + 4) * tilePx (a two-tile quiet zone each side), so subtract 4 tiles per axis.
        int gw = Math.Max(minEdge, displayWidthPx / tilePixelSize - 4);
        int gh = Math.Max(minEdge, displayHeightPx / tilePixelSize - 4);

        var layout = new FrameLayout(gw, gh, tilePixelSize);
        if (maxCodewords > 0 && layout.CodewordCount > maxCodewords)
        {
            double scale = Math.Sqrt((double)maxCodewords / layout.CodewordCount);
            gw = Math.Max(minEdge, (int)(gw * scale));
            gh = Math.Max(minEdge, (int)(gh * scale));
            layout = new FrameLayout(gw, gh, tilePixelSize);

            while (layout.CodewordCount > maxCodewords && gw > minEdge && gh > minEdge)
                layout = new FrameLayout(--gw, --gh, tilePixelSize);
        }

        return layout;
    }

    /// <summary>Gets the role of the tile at the given grid coordinates.</summary>
    /// <param name="x">Tile column.</param>
    /// <param name="y">Tile row.</param>
    public TileRole GetRole(int x, int y)
    {
        ValidateCoordinates(x, y);
        return _roles[y * GridWidthTiles + x];
    }

    /// <summary>Gets the tile positions of one header copy, in symbol order.</summary>
    /// <param name="copyIndex">Header copy index (0-2).</param>
    public IReadOnlyList<(int X, int Y)> GetHeaderCopyTiles(int copyIndex)
    {
        if (copyIndex < 0 || copyIndex >= FrameFormat.HeaderCopyCount)
            throw new ArgumentOutOfRangeException(nameof(copyIndex));
        return _headerCopies[copyIndex];
    }

    /// <summary>Number of codewords carried at a given colour depth (bits per tile).</summary>
    /// <param name="bitsPerTile">Colour depth (1-10).</param>
    public int CodewordsForBits(int bitsPerTile)
    {
        if (bitsPerTile is < 1 or > 10)
            throw new ArgumentOutOfRangeException(nameof(bitsPerTile));
        return DataTileCount * bitsPerTile / (FrameFormat.CodewordLength * 8);
    }

    /// <summary>Maps a data-tile index to its (codeword, symbol) under stride interleaving.</summary>
    /// <param name="dataTileIndex">Index into <see cref="DataTiles"/>.</param>
    public (int Codeword, int Symbol) ToCodewordSymbol(int dataTileIndex)
    {
        if (dataTileIndex < 0 || dataTileIndex >= DataTileCount)
            throw new ArgumentOutOfRangeException(nameof(dataTileIndex));
        return (dataTileIndex % CodewordCount, dataTileIndex / CodewordCount);
    }

    /// <summary>Maps a (codeword, symbol) back to the data-tile index in scan order.</summary>
    /// <param name="codeword">Codeword index.</param>
    /// <param name="symbol">Symbol index within the codeword (0-254).</param>
    public int ToDataTileIndex(int codeword, int symbol)
    {
        if (codeword < 0 || codeword >= CodewordCount)
            throw new ArgumentOutOfRangeException(nameof(codeword));
        if (symbol < 0 || symbol >= FrameFormat.CodewordLength)
            throw new ArgumentOutOfRangeException(nameof(symbol));
        return symbol * CodewordCount + codeword;
    }

    /// <summary>Whether a structural tile (finder or timing) is black. Not valid for other roles.</summary>
    /// <param name="x">Tile column.</param>
    /// <param name="y">Tile row.</param>
    public bool IsStructuralBlack(int x, int y) => GetRole(x, y) switch
    {
        TileRole.Finder => IsFinderBlack(x, y),
        TileRole.Timing => IsTimingBlack(x, y),
        _ => throw new ArgumentException($"Tile ({x},{y}) is not a structural tile."),
    };

    /// <summary>Converts a tile-space coordinate to canonical pixel coordinates.</summary>
    /// <param name="tileX">Tile-space X (fractional addresses within-tile positions).</param>
    /// <param name="tileY">Tile-space Y.</param>
    public (double X, double Y) TileToPixel(double tileX, double tileY) =>
        (QuietZonePx + tileX * TilePixelSize, QuietZonePx + tileY * TilePixelSize);

    private void MarkCornerBlocks()
    {
        foreach (var (blockX, blockY) in CornerBlockOrigins())
            for (int y = blockY; y < blockY + FrameFormat.CornerBlockSizeTiles; y++)
                for (int x = blockX; x < blockX + FrameFormat.CornerBlockSizeTiles; x++)
                    _roles[y * GridWidthTiles + x] = TileRole.Finder;
    }

    private void MarkTimingPattern()
    {
        int block = FrameFormat.CornerBlockSizeTiles;
        for (int x = block; x < GridWidthTiles - block; x++)
            _roles[x] = TileRole.Timing;
        for (int y = block; y < GridHeightTiles - block; y++)
            _roles[y * GridWidthTiles] = TileRole.Timing;
    }

    private (int X, int Y)[][] MarkHeaderCopies()
    {
        int block = FrameFormat.CornerBlockSizeTiles;
        int len = FrameFormat.HeaderCopyLength;
        var copies = new (int X, int Y)[FrameFormat.HeaderCopyCount][];
        copies[0] = MarkHeaderRun(startX: block, startY: 1, stepX: 1, stepY: 0);
        copies[1] = MarkHeaderRun(startX: GridWidthTiles - 1, startY: block, stepX: 0, stepY: 1);
        copies[2] = MarkHeaderRun(startX: GridWidthTiles - block - len, startY: GridHeightTiles - 2, stepX: 1, stepY: 0);
        return copies;
    }

    private (int X, int Y)[] MarkHeaderRun(int startX, int startY, int stepX, int stepY)
    {
        var tiles = new (int X, int Y)[FrameFormat.HeaderCopyLength];
        for (int i = 0; i < FrameFormat.HeaderCopyLength; i++)
        {
            int x = startX + i * stepX;
            int y = startY + i * stepY;
            _roles[y * GridWidthTiles + x] = TileRole.Header;
            tiles[i] = (x, y);
        }

        return tiles;
    }

    private (int X, int Y)[] MarkBeacon()
    {
        var tiles = new (int X, int Y)[16];
        int x0 = GridWidthTiles / 2 - 2;
        int i = 0;
        for (int y = 2; y <= 5; y++)
            for (int x = x0; x < x0 + 4; x++)
            {
                _roles[y * GridWidthTiles + x] = TileRole.Beacon;
                tiles[i++] = (x, y);
            }

        return tiles;
    }

    private int CountNonData()
    {
        int count = 0;
        foreach (var role in _roles)
            if (role != TileRole.Data)
                count++;
        return count;
    }

    private ((int X, int Y)[] Data, (int X, int Y)[] Pad) AssignDataAndPadTiles(int dataTileCount)
    {
        var data = new List<(int X, int Y)>(dataTileCount);
        var pad = new List<(int X, int Y)>();

        for (int y = 0; y < GridHeightTiles; y++)
            for (int x = 0; x < GridWidthTiles; x++)
            {
                if (_roles[y * GridWidthTiles + x] != TileRole.Data)
                    continue;

                if (data.Count < dataTileCount)
                {
                    data.Add((x, y));
                }
                else
                {
                    _roles[y * GridWidthTiles + x] = TileRole.Pad;
                    pad.Add((x, y));
                }
            }

        return (data.ToArray(), pad.ToArray());
    }

    private IEnumerable<(int X, int Y)> CornerBlockOrigins()
    {
        int block = FrameFormat.CornerBlockSizeTiles;
        yield return (0, 0);
        yield return (GridWidthTiles - block, 0);
        yield return (0, GridHeightTiles - block);
        yield return (GridWidthTiles - block, GridHeightTiles - block);
    }

    private bool IsFinderBlack(int x, int y)
    {
        int block = FrameFormat.CornerBlockSizeTiles;
        int finder = FrameFormat.FinderSizeTiles;
        foreach (var (blockX, blockY) in CornerBlockOrigins())
        {
            if (x < blockX || x >= blockX + block || y < blockY || y >= blockY + block)
                continue;

            int finderX = blockX == 0 ? blockX : blockX + 1;
            int finderY = blockY == 0 ? blockY : blockY + 1;
            int localX = x - finderX;
            int localY = y - finderY;

            if (localX < 0 || localX >= finder || localY < 0 || localY >= finder)
                return false;

            bool onBorder = localX == 0 || localX == finder - 1 || localY == 0 || localY == finder - 1;
            bool inCenter = localX >= 2 && localX <= 4 && localY >= 2 && localY <= 4;
            return onBorder || inCenter;
        }

        throw new ArgumentException($"Tile ({x},{y}) is not inside a corner block.");
    }

    private static bool IsTimingBlack(int x, int y) => y == 0 ? x % 2 == 0 : y % 2 == 0;

    private void ValidateCoordinates(int x, int y)
    {
        if (x < 0 || x >= GridWidthTiles)
            throw new ArgumentOutOfRangeException(nameof(x));
        if (y < 0 || y >= GridHeightTiles)
            throw new ArgumentOutOfRangeException(nameof(y));
    }
}
