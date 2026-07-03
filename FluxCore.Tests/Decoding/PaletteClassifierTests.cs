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
        (byte a, byte b, double distance) = FindClosestPalettePair();
        Assert.True(distance < 8, $"Expected a tightly packed pair in the default palette, closest is {distance}.");

        var colorA = ColorMap.Default.GetColor(a);
        var colorB = ColorMap.Default.GetColor(b);
        var midpoint = Classifier.Classify(
            (colorA.R + colorB.R) / 2.0,
            (colorA.G + colorB.G) / 2.0,
            (colorA.B + colorB.B) / 2.0);

        Assert.True(midpoint.AmbiguityRatio > TileClassification.MaxTrustedAmbiguity);
        Assert.True(midpoint.IsLowConfidence);
    }

    private static (byte A, byte B, double Distance) FindClosestPalettePair()
    {
        byte bestA = 0, bestB = 1;
        double best = double.MaxValue;

        for (int i = 0; i < 256; i++)
        {
            var ci = ColorMap.Default.GetColor((byte)i);
            for (int j = i + 1; j < 256; j++)
            {
                var cj = ColorMap.Default.GetColor((byte)j);
                double dr = ci.R - cj.R, dg = ci.G - cj.G, db = ci.B - cj.B;
                double d = Math.Sqrt(dr * dr + dg * dg + db * db);
                if (d < best)
                {
                    best = d;
                    bestA = (byte)i;
                    bestB = (byte)j;
                }
            }
        }

        return (bestA, bestB, best);
    }
}
