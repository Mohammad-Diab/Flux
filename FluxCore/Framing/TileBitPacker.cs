namespace FluxCore.Framing;

/// <summary>
/// Packs a byte stream into fixed-width tile values and back, MSB-first: the first bit of the
/// stream becomes the most-significant bit of the first tile value. This lets a frame carry data
/// at a chosen colour depth — 8 bits per tile is one byte per tile; fewer bits pack several tiles
/// per byte and more bits (up to 10, for a 1024-colour palette) carry more per tile. Tile values
/// are <see cref="ushort"/> so depths above 8 fit. The metadata frame uses this at 3 bits per tile.
/// </summary>
public static class TileBitPacker
{
    /// <summary>Number of tile values needed to carry <paramref name="byteCount"/> bytes at the given depth.</summary>
    public static int TileCount(int byteCount, int bitsPerTile)
    {
        ValidateDepth(bitsPerTile);
        return (byteCount * 8 + bitsPerTile - 1) / bitsPerTile;
    }

    /// <summary>Packs a byte stream into tile values of <paramref name="bitsPerTile"/> bits each (MSB-first).</summary>
    /// <param name="data">Bytes to pack.</param>
    /// <param name="bitsPerTile">Bits carried per tile (1–10).</param>
    public static ushort[] Pack(ReadOnlySpan<byte> data, int bitsPerTile)
    {
        ValidateDepth(bitsPerTile);

        int totalBits = data.Length * 8;
        var tiles = new ushort[TileCount(data.Length, bitsPerTile)];
        for (int t = 0; t < tiles.Length; t++)
        {
            int value = 0;
            int baseBit = t * bitsPerTile;
            for (int k = 0; k < bitsPerTile; k++)
            {
                int globalBit = baseBit + k;
                int bit = globalBit < totalBits ? (data[globalBit >> 3] >> (7 - (globalBit & 7))) & 1 : 0;
                value |= bit << (bitsPerTile - 1 - k);
            }

            tiles[t] = (ushort)value;
        }

        return tiles;
    }

    /// <summary>Unpacks tile values back into <paramref name="byteCount"/> bytes (inverse of <see cref="Pack"/>).</summary>
    /// <param name="tiles">Tile values to unpack.</param>
    /// <param name="bitsPerTile">Bits carried per tile (1–10).</param>
    /// <param name="byteCount">Number of bytes to recover.</param>
    public static byte[] Unpack(ReadOnlySpan<ushort> tiles, int bitsPerTile, int byteCount)
    {
        ValidateDepth(bitsPerTile);

        int totalBits = byteCount * 8;
        var data = new byte[byteCount];
        for (int t = 0; t < tiles.Length; t++)
        {
            int baseBit = t * bitsPerTile;
            for (int k = 0; k < bitsPerTile; k++)
            {
                int globalBit = baseBit + k;
                if (globalBit >= totalBits)
                    return data;
                if (((tiles[t] >> (bitsPerTile - 1 - k)) & 1) != 0)
                    data[globalBit >> 3] |= (byte)(1 << (7 - (globalBit & 7)));
            }
        }

        return data;
    }

    private static void ValidateDepth(int bitsPerTile)
    {
        if (bitsPerTile is < 1 or > 10)
            throw new ArgumentOutOfRangeException(nameof(bitsPerTile), "Bits per tile must be between 1 and 10.");
    }
}
