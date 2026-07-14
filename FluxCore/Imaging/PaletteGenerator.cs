using System.Numerics;

namespace FluxCore.Imaging;

/// <summary>A generated data-tile palette: its colours and the minimum pairwise RGB distance
/// achieved (a lower distance is denser but less robust to lossy capture).</summary>
/// <param name="Colors">The palette colours, in byte-index order.</param>
/// <param name="MinimumDistance">Smallest Euclidean RGB distance between any two colours.</param>
public readonly record struct GeneratedPalette(Rgb24[] Colors, double MinimumDistance);

/// <summary>
/// Deterministically generates a data-tile palette from just a colour count, so sender and receiver
/// derive the identical palette without a colour list ever crossing the channel. Colours sit on a
/// balanced RGB lattice: the count's bit budget is split across axes as evenly as possible (extra
/// levels to red then green, fewest to the compression-vulnerable blue axis), each axis spread
/// evenly over 0–255. White is reserved for structural tiles, so a lattice point that lands on
/// white is replaced by a fixed gap colour. <see cref="Generate(int)"/> at 256 reproduces the
/// historical default palette exactly. The rugged kind instead lays 8 grays on a luma ladder.
/// </summary>
public static class PaletteGenerator
{
    /// <summary>Smallest supported colour count.</summary>
    public const int MinColorCount = 8;

    /// <summary>Largest supported colour count (10 bits per tile).</summary>
    public const int MaxColorCount = 1024;

    /// <summary>The only colour count the rugged tier supports (3 bits per tile).</summary>
    public const int RuggedColorCount = 8;

    private static readonly Rgb24 WhiteReplacement = new(18, 18, 43);

    /// <summary>Determines whether a colour count is supported for the standard tier (a power of two in [8, 1024]).</summary>
    /// <param name="colorCount">Number of colours.</param>
    public static bool IsSupportedCount(int colorCount) => IsSupportedCount(colorCount, PaletteKind.Standard);

    /// <summary>Determines whether a colour count is supported for a palette kind.</summary>
    /// <param name="colorCount">Number of colours.</param>
    /// <param name="kind">Palette family.</param>
    public static bool IsSupportedCount(int colorCount, PaletteKind kind) => kind switch
    {
        PaletteKind.Rugged => colorCount == RuggedColorCount,
        _ => colorCount is >= MinColorCount and <= MaxColorCount && (colorCount & (colorCount - 1)) == 0,
    };

    /// <summary>Bits per tile carried by a colour count (its base-2 log; 256→8, 512→9, 1024→10).</summary>
    /// <param name="colorCount">A supported colour count.</param>
    public static int BitsForCount(int colorCount)
    {
        if (!IsSupportedCount(colorCount))
            throw new ArgumentOutOfRangeException(nameof(colorCount));
        return BitOperations.Log2((uint)colorCount);
    }

    /// <summary>Generates the standard-tier palette for a colour count (a power of two in [8, 1024]).</summary>
    /// <param name="colorCount">Number of colours.</param>
    public static GeneratedPalette Generate(int colorCount) => Generate(colorCount, PaletteKind.Standard);

    /// <summary>Generates the palette for a colour count and kind.</summary>
    /// <param name="colorCount">Number of colours.</param>
    /// <param name="kind">Palette family (standard lattice or rugged grayscale ladder).</param>
    public static GeneratedPalette Generate(int colorCount, PaletteKind kind)
    {
        if (!IsSupportedCount(colorCount, kind))
            throw new ArgumentOutOfRangeException(nameof(colorCount), kind == PaletteKind.Rugged
                ? $"The rugged tier supports only {RuggedColorCount} colours."
                : $"Colour count must be a power of two between {MinColorCount} and {MaxColorCount}.");

        if (kind == PaletteKind.Rugged)
            return GenerateRugged();

        int bits = BitOperations.Log2((uint)colorCount);
        var (levelsR, levelsG, levelsB) = AxisLevelCounts(bits);
        var red = SpreadLevels(levelsR);
        var green = SpreadLevels(levelsG);
        var blue = SpreadLevels(levelsB);

        var colors = new Rgb24[colorCount];
        int i = 0;
        for (int r = 0; r < levelsR; r++)
            for (int g = 0; g < levelsG; g++)
                for (int b = 0; b < levelsB; b++)
                {
                    var color = new Rgb24(red[r], green[g], blue[b]);
                    colors[i++] = color.IsWhite ? WhiteReplacement : color;
                }

        return new GeneratedPalette(colors, MinimumPairwiseDistance(colors));
    }

    // Eight grays on R=G=B — the first 8 of 9 levels evenly spread over 0..255, so the 9th (255,
    // reserved white) stays a null-tile marker. Entries differ only in luma, which screen codecs
    // preserve, so chroma loss can't merge them.
    private static GeneratedPalette GenerateRugged()
    {
        var colors = new Rgb24[RuggedColorCount];
        for (int i = 0; i < RuggedColorCount; i++)
        {
            byte v = (byte)((i * 255 + RuggedColorCount / 2) / RuggedColorCount); // round(i·255 / 8)
            colors[i] = new Rgb24(v, v, v);
        }

        return new GeneratedPalette(colors, MinimumPairwiseDistance(colors));
    }

    // Split the bit budget evenly; the remainder goes to red then green, leaving blue the fewest.
    private static (int R, int G, int B) AxisLevelCounts(int bits)
    {
        int baseBits = bits / 3;
        int remainder = bits % 3;
        int rBits = baseBits + (remainder > 0 ? 1 : 0);
        int gBits = baseBits + (remainder > 1 ? 1 : 0);
        return (1 << rBits, 1 << gBits, 1 << baseBits);
    }

    private static byte[] SpreadLevels(int count)
    {
        var levels = new byte[count];
        for (int j = 0; j < count; j++)
            levels[j] = (byte)((j * 255 + (count - 1) / 2) / (count - 1)); // round(j·255 / (count-1))
        return levels;
    }

    /// <summary>Smallest Euclidean RGB distance between any two colours in a palette.</summary>
    /// <param name="colors">Palette colours.</param>
    internal static double MinimumPairwiseDistance(Rgb24[] colors)
    {
        double minSquared = double.MaxValue;
        for (int a = 0; a < colors.Length; a++)
            for (int c = a + 1; c < colors.Length; c++)
            {
                double dr = colors[a].R - colors[c].R;
                double dg = colors[a].G - colors[c].G;
                double db = colors[a].B - colors[c].B;
                double d2 = dr * dr + dg * dg + db * db;
                if (d2 < minSquared)
                    minSquared = d2;
            }

        return Math.Sqrt(minSquared);
    }
}
