using FluxCore.Imaging;

namespace FluxCore.Decoding;

/// <summary>
/// Result of classifying a sampled tile color against the palette.
/// </summary>
/// <param name="PaletteIndex">Index of the nearest palette color.</param>
/// <param name="NearestDistance">Euclidean RGB distance to the nearest palette color.</param>
/// <param name="AmbiguityRatio">Ratio of nearest to second-nearest distance (near 1 = ambiguous).</param>
/// <param name="IsLowConfidence">Whether this classification should not be trusted (set by the classifier).</param>
public readonly record struct TileClassification(
    ushort PaletteIndex, double NearestDistance, double AmbiguityRatio, bool IsLowConfidence);

/// <summary>
/// Classifies sampled RGB colors to the nearest palette entry with a confidence estimate, so the
/// decoder can refuse to attempt error correction on unstable captures. The distance trust gate
/// scales with palette density (a fraction of the palette's minimum pairwise distance) so a dense
/// 512/1024-colour palette is judged more strictly than the 256-colour default.
/// </summary>
public sealed class PaletteClassifier
{
    /// <summary>Trust gate as a fraction of the palette's minimum pairwise distance (256 → 36 × 2/3 = 24).</summary>
    public const double TrustedDistanceFraction = 2.0 / 3.0;

    /// <summary>Maximum trusted nearest/second-nearest distance ratio (density-independent).</summary>
    public const double MaxTrustedAmbiguity = 0.7;

    private readonly double[] _r;
    private readonly double[] _g;
    private readonly double[] _b;
    private readonly int _count;

    /// <summary>Creates a classifier for the given palette.</summary>
    public PaletteClassifier(ColorMap colorMap)
    {
        ArgumentNullException.ThrowIfNull(colorMap);

        _count = colorMap.Count;
        _r = new double[_count];
        _g = new double[_count];
        _b = new double[_count];
        for (int i = 0; i < _count; i++)
        {
            var color = colorMap.GetColor(i);
            _r[i] = color.R;
            _g[i] = color.G;
            _b[i] = color.B;
        }

        MaxTrustedDistance = colorMap.MinimumDistance * TrustedDistanceFraction;
    }

    /// <summary>Gets the maximum trusted distance to the nearest palette colour, scaled to this palette's density.</summary>
    public double MaxTrustedDistance { get; }

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

        for (int i = 0; i < _count; i++)
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
        bool lowConfidence = nearest > MaxTrustedDistance || ambiguity > MaxTrustedAmbiguity;

        return new TileClassification((ushort)bestIndex, nearest, ambiguity, lowConfidence);
    }
}
