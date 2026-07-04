namespace FluxCore.Framing;

/// <summary>
/// Color scheme used for a frame's data and header tiles.
/// </summary>
public enum TileColorScheme
{
    /// <summary>256-color palette, one byte per tile. Used by payload frames (1..N).</summary>
    Palette256,

    /// <summary>Eight RGB cube corners, three bits per tile. Used by the metadata frame (frame 0).</summary>
    CubeCorner8,
}

/// <summary>
/// Complete abstract description of one frame: the per-tile value for data and header tiles plus
/// the beacon parity and color scheme. Structural tile colors (finders, timing, pad) are fixed by
/// <see cref="FrameFormat"/> and carried implicitly. Consumed by the frame renderer.
/// </summary>
public sealed class FrameTileMap
{
    private readonly byte[] _tileValues;

    /// <summary>Gets the header this frame was built from.</summary>
    public FrameHeader Header { get; }

    /// <summary>Gets the color scheme for this frame's data and header tiles.</summary>
    public TileColorScheme ColorScheme { get; }

    /// <summary>Gets a value indicating whether the beacon block renders black (frame id is even) or white (odd).</summary>
    public bool BeaconIsBlack => Header.FrameId % 2 == 0;

    /// <summary>
    /// Initializes a new instance of the <see cref="FrameTileMap"/> class.
    /// </summary>
    /// <param name="header">Frame header.</param>
    /// <param name="tileValues">Per-tile value (row-major, 14,400 entries); only Data and Header tiles are meaningful. Palette indices for Palette256, 0-7 cube-corner indices for CubeCorner8.</param>
    /// <param name="colorScheme">Color scheme for data and header tiles.</param>
    public FrameTileMap(FrameHeader header, byte[] tileValues, TileColorScheme colorScheme = TileColorScheme.Palette256)
    {
        ArgumentNullException.ThrowIfNull(tileValues);
        if (tileValues.Length != FrameFormat.TotalTiles)
            throw new ArgumentException(
                $"Tile values must have {FrameFormat.TotalTiles} entries.", nameof(tileValues));

        Header = header;
        _tileValues = tileValues;
        ColorScheme = colorScheme;
    }

    /// <summary>
    /// Gets the encoded value of the tile at the given grid coordinates.
    /// Only meaningful for tiles whose role is Data or Header.
    /// </summary>
    /// <param name="x">Tile column (0-159).</param>
    /// <param name="y">Tile row (0-89).</param>
    public byte GetTileValue(int x, int y) => _tileValues[y * FrameFormat.GridWidthTiles + x];
}
