using FluxCore.Decoding;
using Xunit;

namespace FluxCore.Tests.Decoding;

public class HomographyTests
{
    private static readonly (double X, double Y)[] UnitSquare = [(0, 0), (1, 0), (0, 1), (1, 1)];

    [Fact]
    public void Identity_MapsPointsToThemselves()
    {
        var h = Homography.FromPoints(UnitSquare, UnitSquare);

        var (x, y) = h.Map(0.25, 0.75);

        Assert.Equal(0.25, x, precision: 9);
        Assert.Equal(0.75, y, precision: 9);
    }

    [Fact]
    public void TranslationAndScale_MapsAffinely()
    {
        (double X, double Y)[] destination = [(10, 20), (30, 20), (10, 60), (30, 60)];
        var h = Homography.FromPoints(UnitSquare, destination);

        var (x, y) = h.Map(0.5, 0.5);

        Assert.Equal(20, x, precision: 9);
        Assert.Equal(40, y, precision: 9);
    }

    [Fact]
    public void Projective_MapsControlPointsExactly()
    {
        (double X, double Y)[] destination = [(3, 5), (105, 12), (8, 92), (98, 100)];
        var h = Homography.FromPoints(UnitSquare, destination);

        for (int i = 0; i < 4; i++)
        {
            var (x, y) = h.Map(UnitSquare[i].X, UnitSquare[i].Y);
            Assert.Equal(destination[i].X, x, precision: 6);
            Assert.Equal(destination[i].Y, y, precision: 6);
        }
    }

    [Fact]
    public void ForwardThenInverse_RoundTrips()
    {
        (double X, double Y)[] destination = [(3, 5), (105, 12), (8, 92), (98, 100)];
        var forward = Homography.FromPoints(UnitSquare, destination);
        var inverse = Homography.FromPoints(destination, UnitSquare);

        var mapped = forward.Map(0.3, 0.6);
        var (x, y) = inverse.Map(mapped.X, mapped.Y);

        Assert.Equal(0.3, x, precision: 6);
        Assert.Equal(0.6, y, precision: 6);
    }

    [Fact]
    public void CollinearPoints_Throw()
    {
        (double X, double Y)[] collinear = [(0, 0), (1, 1), (2, 2), (3, 3)];

        Assert.Throws<ArgumentException>(() => Homography.FromPoints(collinear, UnitSquare));
    }
}
