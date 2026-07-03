using FluxCore.Decoding;
using FluxCore.Imaging;
using Xunit;

namespace FluxCore.Tests.Decoding;

public class PaletteClassifierTests
{
    private static readonly PaletteClassifier Classifier = new(ColorMap.Default);

    [Fact]
    public void Classify_ExactPaletteColors_ReturnsIndexWithZeroDistance()
    {
        for (int i = 0; i < 256; i++)
        {
            var color = ColorMap.Default.GetColor((byte)i);
            var result = Classifier.Classify(color.R, color.G, color.B);

            Assert.Equal((byte)i, result.PaletteIndex);
            Assert.Equal(0, result.NearestDistance, precision: 9);
            Assert.False(result.IsLowConfidence);
        }
    }

    [Fact]
    public void Classify_SlightlyPerturbedDistinctColor_StillConfident()
    {
        var color = ColorMap.Default.GetColor(100);

        var result = Classifier.Classify(color.R + 3, Math.Max(0, color.G - 2), color.B + 1);

        Assert.Equal(100, result.PaletteIndex);
        Assert.False(result.IsLowConfidence);
    }

    [Fact]
    public void Classify_White_IsLowConfidence()
    {
        var result = Classifier.Classify(255, 255, 255);

        Assert.True(result.IsLowConfidence);
        Assert.True(result.NearestDistance > TileClassification.MaxTrustedDistance);
    }

    [Fact]
    public void Classify_BetweenTwoNearNeighbors_FlagsAmbiguity()
    {
        var palette = ColorMap.Default.Palette.ToArray();
        palette[0] = new Rgb24(100, 100, 100);
        palette[1] = new Rgb24(102, 100, 100);
        var cramped = new PaletteClassifier(new ColorMap(palette));

        var midpoint = cramped.Classify(101, 100, 100);

        Assert.True(midpoint.AmbiguityRatio > TileClassification.MaxTrustedAmbiguity);
        Assert.True(midpoint.IsLowConfidence);
    }

    [Fact]
    public void DefaultPalette_MinimumPairwiseDistance_SupportsLossyCapture()
    {
        double best = double.MaxValue;

        for (int i = 0; i < 256; i++)
        {
            var ci = ColorMap.Default.GetColor((byte)i);
            for (int j = i + 1; j < 256; j++)
            {
                var cj = ColorMap.Default.GetColor((byte)j);
                double dr = ci.R - cj.R, dg = ci.G - cj.G, db = ci.B - cj.B;
                best = Math.Min(best, Math.Sqrt(dr * dr + dg * dg + db * db));
            }
        }

        Assert.True(best >= 30, $"Default palette minimum pairwise distance is {best:F1}; must stay >= 30.");
    }
}
