using FluxCore.Imaging;

namespace FluxCore.Decoding;

/// <summary>
/// Result of classifying a sampled tile color against the palette.
/// </summary>
/// <param name="PaletteIndex">Index of the nearest palette color.</param>
/// <param name="NearestDistance">Euclidean RGB distance to the nearest palette color.</param>
/// <param name="AmbiguityRatio">Ratio of nearest to second-nearest distance (near 1 = ambiguous).</param>
public readonly record struct TileClassification(byte PaletteIndex, double NearestDistance, double AmbiguityRatio)
{
    /// <summary>Maximum trusted distance to the nearest palette color.</summary>
    public const double MaxTrustedDistance = 24;

    /// <summary>Maximum trusted nearest/second-nearest distance ratio.</summary>
    public const double MaxTrustedAmbiguity = 0.7;

    /// <summary>Gets a value indicating whether this classification should not be trusted.</summary>
    public bool IsLowConfidence =>
        NearestDistance > MaxTrustedDistance || AmbiguityRatio > MaxTrustedAmbiguity;
}

/// <summary>
/// Classifies sampled RGB colors to the nearest palette entry with a confidence estimate,
/// so the decoder can refuse to attempt error correction on unstable captures.
/// </summary>
public sealed class PaletteClassifier
{
    private readonly double[] _r = new double[256];
    private readonly double[] _g = new double[256];
    private readonly double[] _b = new double[256];

    /// <summary>
    /// Initializes a new instance of the <see cref="PaletteClassifier"/> class.
    /// </summary>
    /// <param name="colorMap">Palette to classify against.</param>
    public PaletteClassifier(ColorMap colorMap)
    {
        ArgumentNullException.ThrowIfNull(colorMap);

        for (int i = 0; i < 256; i++)
        {
            var color = colorMap.GetColor((byte)i);
            _r[i] = color.R;
            _g[i] = color.G;
            _b[i] = color.B;
        }
    }

    /// <summary>
    /// Classifies a sampled mean color to the nearest palette entry.
    /// </summary>
    /// <param name="r">Mean red component.</param>
    /// <param name="g">Mean green component.</param>
    /// <param name="b">Mean blue component.</param>
    public TileClassification Classify(double r, double g, double b)
    {
        double best = double.MaxValue;
        double secondBest = double.MaxValue;
        int bestIndex = 0;

        for (int i = 0; i < 256; i++)
        {
            double dr = r - _r[i];
            double dg = g - _g[i];
            double db = b - _b[i];
            double distanceSquared = dr * dr + dg * dg + db * db;

            if (distanceSquared < best)
            {
                secondBest = best;
                best = distanceSquared;
                bestIndex = i;
            }
            else if (distanceSquared < secondBest)
            {
                secondBest = distanceSquared;
            }
        }

        double nearest = Math.Sqrt(best);
        double second = Math.Sqrt(secondBest);
        double ambiguity = second < 1e-9 ? 1 : nearest / second;

        return new TileClassification((byte)bestIndex, nearest, ambiguity);
    }
}
