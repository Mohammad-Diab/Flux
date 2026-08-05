namespace FluxCore.Imaging;

/// <summary>
/// The two-level black/white scheme used to encode the metadata frame (frame 0) at 1 bit per
/// tile. Classification is a single luma threshold, so frame 0 survives any channel that can
/// show a frame at all — grayscale links, chroma-destroying codecs, and low-contrast captures
/// that would defeat every colour palette.
/// </summary>
public static class MonoColors
{
    /// <summary>Number of distinct levels.</summary>
    public const int Count = 2;

    /// <summary>Bits carried per tile.</summary>
    public const int BitsPerTile = 1;

    /// <summary>Gets the color for a 1-bit index: 0 is black, 1 is white.</summary>
    /// <param name="index">Bit value (0-1).</param>
    public static Rgb24 ToColor(int index)
    {
        if (index is < 0 or >= Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        return index == 0 ? new Rgb24(0, 0, 0) : new Rgb24(255, 255, 255);
    }

    /// <summary>Classifies a sampled tile by luma against the capture's adaptive threshold.</summary>
    /// <param name="luma">Mean tile luma.</param>
    /// <param name="threshold">Black/white threshold for this capture.</param>
    public static int Classify(double luma, double threshold) => luma > threshold ? 1 : 0;
}
