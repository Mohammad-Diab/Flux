using FluxCore.Imaging;
using Xunit;

namespace FluxCore.Tests.Imaging;

public class CubeCornerColorsTests
{
    [Fact]
    public void ToColor_ProducesTheEightCubeCorners()
    {
        Assert.Equal(new Rgb24(0, 0, 0), CubeCornerColors.ToColor(0));
        Assert.Equal(new Rgb24(0, 0, 255), CubeCornerColors.ToColor(1));
        Assert.Equal(new Rgb24(0, 255, 0), CubeCornerColors.ToColor(2));
        Assert.Equal(new Rgb24(0, 255, 255), CubeCornerColors.ToColor(3));
        Assert.Equal(new Rgb24(255, 0, 0), CubeCornerColors.ToColor(4));
        Assert.Equal(new Rgb24(255, 0, 255), CubeCornerColors.ToColor(5));
        Assert.Equal(new Rgb24(255, 255, 0), CubeCornerColors.ToColor(6));
        Assert.Equal(new Rgb24(255, 255, 255), CubeCornerColors.ToColor(7));
    }

    [Fact]
    public void Classify_IsInverseOfToColor_ForAllIndices()
    {
        for (int i = 0; i < CubeCornerColors.Count; i++)
        {
            var c = CubeCornerColors.ToColor(i);
            Assert.Equal(i, CubeCornerColors.Classify(c.R, c.G, c.B));
        }
    }

    [Fact]
    public void Classify_ToleratesLargeCompressionShift()
    {
        Assert.Equal(4, CubeCornerColors.Classify(238, 14, 9));
        Assert.Equal(3, CubeCornerColors.Classify(20, 240, 235));
        Assert.Equal(0, CubeCornerColors.Classify(60, 50, 40));
        Assert.Equal(7, CubeCornerColors.Classify(200, 210, 190));
    }

    [Fact]
    public void MinimumPairwiseDistance_Is255()
    {
        double min = double.MaxValue;
        for (int i = 0; i < CubeCornerColors.Count; i++)
        {
            var a = CubeCornerColors.ToColor(i);
            for (int j = i + 1; j < CubeCornerColors.Count; j++)
            {
                var b = CubeCornerColors.ToColor(j);
                double d = Math.Sqrt(
                    Math.Pow(a.R - b.R, 2) + Math.Pow(a.G - b.G, 2) + Math.Pow(a.B - b.B, 2));
                min = Math.Min(min, d);
            }
        }

        Assert.Equal(255, min);
    }
}
