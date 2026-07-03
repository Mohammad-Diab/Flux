namespace FluxCore.Framing;

/// <summary>
/// Supported tile sizes in pixels (square tiles).
/// </summary>
public enum TileSize : byte
{
    /// <summary>2×2 pixels per tile.</summary>
    Size2x2 = 2,

    /// <summary>4×4 pixels per tile.</summary>
    Size4x4 = 4,

    /// <summary>6×6 pixels per tile.</summary>
    Size6x6 = 6,

/// <summary>8×8 pixels per tile.</summary>
    Size8x8 = 8
}

/// <summary>
/// Frame layout configuration and calculations.
/// </summary>
public sealed class FrameLayout
{
    private const int MinFrameWidthPx = 800;
    private const int MinFrameHeightPx = 600;

    /// <summary>
    /// Gets the tile size in pixels.
    /// </summary>
    public TileSize TileSize { get; }

    /// <summary>
    /// Gets the tile size as an integer.
    /// </summary>
  public int TileSizePx => (int)TileSize;

    /// <summary>
  /// Gets the margin width in pixels (¼ tile width, ?1 px).
    /// </summary>
    public int MarginPx { get; }

    /// <summary>
    /// Gets the separator width in pixels (½ tile width, ?2 px).
    /// </summary>
    public int SeparatorPx { get; }

    /// <summary>
    /// Gets the separator interval in tiles (default: every 8 tiles).
    /// </summary>
    public int SeparatorEvery { get; }

    /// <summary>
    /// Gets the frame width in pixels.
    /// </summary>
    public int FrameWidthPx { get; }

    /// <summary>
    /// Gets the frame height in pixels.
    /// </summary>
    public int FrameHeightPx { get; }

  /// <summary>
    /// Gets the frame width in tiles (excludingmargins/separators).
    /// </summary>
    public int FrameWidthTiles { get; }

    /// <summary>
    /// Gets the frame height in tiles (excluding margins/separators).
    /// </summary>
    public int FrameHeightTiles { get; }

    /// <summary>
    /// Gets the total capacity in tiles per frame.
    /// </summary>
    public int TotalTiles => FrameWidthTiles * FrameHeightTiles;

    /// <summary>
    /// Initializes a new instance of the <see cref="FrameLayout"/> class.
    /// </summary>
    /// <param name="frameWidthPx">Frame width in pixels (min 800).</param>
  /// <param name="frameHeightPx">Frame height in pixels (min 600).</param>
    /// <param name="tileSize">Tile size.</param>
    /// <param name="separatorEvery">Separator interval in tiles (default: 8).</param>
    public FrameLayout(int frameWidthPx, int frameHeightPx, TileSize tileSize, int separatorEvery = 8)
    {
        if (frameWidthPx < MinFrameWidthPx)
            throw new ArgumentException($"Frame width must be at least {MinFrameWidthPx} px.", nameof(frameWidthPx));

        if (frameHeightPx < MinFrameHeightPx)
       throw new ArgumentException($"Frame height must be at least {MinFrameHeightPx} px.", nameof(frameHeightPx));

        if (separatorEvery < 1)
throw new ArgumentException("Separator interval must be at least 1.", nameof(separatorEvery));

        TileSize = tileSize;
    FrameWidthPx = frameWidthPx;
        FrameHeightPx = frameHeightPx;
  SeparatorEvery = separatorEvery;

        // Calculate margins and separators
MarginPx = Math.Max(1, TileSizePx / 4);
        SeparatorPx = Math.Max(2, TileSizePx / 2);

        // Calculate tile grid
     var (widthTiles, heightTiles) = CalculateTileGrid();
        FrameWidthTiles = widthTiles;
    FrameHeightTiles = heightTiles;
    }

    private (int widthTiles, int heightTiles) CalculateTileGrid()
    {
        // Available space = FrameSize - 2*Margin
int availableWidth = FrameWidthPx - 2 * MarginPx;
        int availableHeight = FrameHeightPx - 2 * MarginPx;

        // Calculate how many tiles fit, accounting for separators
        int widthTiles = CalculateTilesInDimension(availableWidth, TileSizePx, SeparatorPx, SeparatorEvery);
 int heightTiles = CalculateTilesInDimension(availableHeight, TileSizePx, SeparatorPx, SeparatorEvery);

        return (widthTiles, heightTiles);
    }

    private static int CalculateTilesInDimension(int availablePx, int tileSizePx, int separatorPx, int separatorEvery)
    {
// Start with maximum possible tiles (ignoring separators)
        int maxTiles = availablePx / tileSizePx;

  // Count separators needed
        int separatorCount = maxTiles / separatorEvery;
        int separatorSpacePx = separatorCount * separatorPx;

     // Adjust for separator space
        int adjustedAvailable = availablePx - separatorSpacePx;
        int tiles = Math.Max(0, adjustedAvailable / tileSizePx);

     return tiles;
    }

    /// <summary>
    /// Calculates the pixel position of a tile.
    /// </summary>
    /// <param name="tileX">Tile X coordinate (0-based).</param>
    /// <param name="tileY">Tile Y coordinate (0-based).</param>
    /// <returns>Pixel coordinates (top-left corner of tile).</returns>
    public (int pixelX, int pixelY) GetTilePosition(int tileX, int tileY)
    {
        if (tileX < 0 || tileX >= FrameWidthTiles)
            throw new ArgumentOutOfRangeException(nameof(tileX));

        if (tileY < 0 || tileY >= FrameHeightTiles)
            throw new ArgumentOutOfRangeException(nameof(tileY));

      int pixelX = MarginPx + tileX * TileSizePx + (tileX / SeparatorEvery) * SeparatorPx;
        int pixelY = MarginPx + tileY * TileSizePx + (tileY / SeparatorEvery) * SeparatorPx;

        return (pixelX, pixelY);
    }
}
