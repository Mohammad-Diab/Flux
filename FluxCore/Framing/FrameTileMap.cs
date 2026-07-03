

namespace FluxCore.Framing;

/// <summary>
/// Complete abstract description of one frame: palette values for data and header tiles plus
/// the beacon parity. Structural tile colors (finders, timing, pad) are fixed by
/// <see cref="FrameFormat"/> and carried implicitly. Input to <see cref="FrameRenderer"/>.
/// </summary>
public sealed class FrameTileMap
{
    private readonly byte[] _paletteValues;

    /// <summary>Gets the header this frame was built from.</summary>
    public FrameHeader Header { get; }

    /// <summary>Gets a value indicating whether the beacon block renders black (frame id is even) or white (odd).</summary>
    public bool BeaconIsBlack => Header.FrameId % 2 == 0;

    /// <summary>
    /// Initializes a new instance of the <see cref="FrameTileMap"/> class.
    /// </summary>
    /// <param name="header">Frame header.</param>
    /// <param name="paletteValues">Palette byte per tile (row-major, 14,400 entries); only Data and Header tiles are meaningful.</param>
    public FrameTileMap(FrameHeader header, byte[] paletteValues)
    {
        ArgumentNullException.ThrowIfNull(paletteValues);
        if (paletteValues.Length != FrameFormat.TotalTiles)
            throw new ArgumentException(
                $"Palette values must have {FrameFormat.TotalTiles} entries.", nameof(paletteValues));

        Header = header;
        _paletteValues = paletteValues;
    }

    /// <summary>
    /// Gets the palette byte of the tile at the given grid coordinates.
    /// Only meaningful for tiles whose role is Data or Header.
    /// </summary>
    /// <param name="x">Tile column (0-159).</param>
    /// <param name="y">Tile row (0-89).</param>
    public byte GetPaletteValue(int x, int y) => _paletteValues[y * FrameFormat.GridWidthTiles + x];
}
