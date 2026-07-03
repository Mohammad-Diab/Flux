namespace FluxCore.Imaging;

/// <summary>
/// The eight RGB cube-corner colors used to encode the metadata frame (frame 0) at 3 bits per
/// tile. Each color is a corner of the RGB cube, so the minimum pairwise distance is 255 and
/// classification is three independent per-channel threshold checks — maximally robust under
/// lossy capture. Index bits are R (bit 2), G (bit 1), B (bit 0). White (index 7) is used as a
/// data color on frame 0 only; elsewhere white stays reserved for null and structural tiles.
/// </summary>
public static class CubeCornerColors
{
    /// <summary>Number of distinct colors.</summary>
    public const int Count = 8;

    /// <summary>Bits carried per tile.</summary>
    public const int BitsPerTile = 3;

    private const double Threshold = 127.5;

    /// <summary>Gets the color for a 3-bit index (0-7).</summary>
    /// <param name="index">Color index (0-7).</param>
    public static Rgb24 ToColor(int index)
    {
        if (index is < 0 or >= Count)
            throw new ArgumentOutOfRangeException(nameof(index));

        byte r = (index & 0b100) != 0 ? (byte)255 : (byte)0;
        byte g = (index & 0b010) != 0 ? (byte)255 : (byte)0;
        byte b = (index & 0b001) != 0 ? (byte)255 : (byte)0;
        return new Rgb24(r, g, b);
    }

    /// <summary>Classifies a sampled mean color to the nearest cube corner via per-channel thresholding.</summary>
    /// <param name="r">Mean red component.</param>
    /// <param name="g">Mean green component.</param>
    /// <param name="b">Mean blue component.</param>
    public static int Classify(double r, double g, double b)
    {
        int index = 0;
        if (r > Threshold) index |= 0b100;
        if (g > Threshold) index |= 0b010;
        if (b > Threshold) index |= 0b001;
        return index;
    }
}
