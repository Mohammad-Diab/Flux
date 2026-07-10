using FluxCore.Framing;
using Xunit;

namespace FluxCore.Tests.Framing;

public class FrameLayoutTests
{
    private static readonly FrameLayout Default = FrameLayout.Default;

    [Fact]
    public void Default_GeometryMatchesFrameFormat()
    {
        Assert.Equal(FrameFormat.GridWidthTiles, Default.GridWidthTiles);
        Assert.Equal(FrameFormat.GridHeightTiles, Default.GridHeightTiles);
        Assert.Equal(FrameFormat.TilePixelSize, Default.TilePixelSize);
        Assert.Equal(FrameFormat.TotalTiles, Default.TotalTiles);
        Assert.Equal(FrameFormat.QuietZonePx, Default.QuietZonePx);
        Assert.Equal(FrameFormat.FrameWidthPx, Default.FrameWidthPx);
        Assert.Equal(FrameFormat.FrameHeightPx, Default.FrameHeightPx);
        Assert.Equal(FrameFormat.CodewordCount, Default.CodewordCount);
        Assert.Equal(FrameFormat.DataTileCount, Default.DataTileCount);
    }

    [Fact]
    public void Default_TileRolesMatchFrameFormat()
    {
        for (int y = 0; y < FrameFormat.GridHeightTiles; y++)
            for (int x = 0; x < FrameFormat.GridWidthTiles; x++)
                Assert.Equal(FrameFormat.GetRole(x, y), Default.GetRole(x, y));
    }

    [Fact]
    public void Default_PositionListsMatchFrameFormat()
    {
        Assert.Equal(FrameFormat.DataTiles, Default.DataTiles);
        Assert.Equal(FrameFormat.PadTiles, Default.PadTiles);
        Assert.Equal(FrameFormat.BeaconTiles, Default.BeaconTiles);
        Assert.Equal(FrameFormat.FinderCentersTiles, Default.FinderCentersTiles);
        for (int copy = 0; copy < FrameFormat.HeaderCopyCount; copy++)
            Assert.Equal(FrameFormat.GetHeaderCopyTiles(copy), Default.GetHeaderCopyTiles(copy));
    }

    [Fact]
    public void Default_StructuralColorsMatchFrameFormat()
    {
        for (int y = 0; y < FrameFormat.GridHeightTiles; y++)
            for (int x = 0; x < FrameFormat.GridWidthTiles; x++)
            {
                var role = FrameFormat.GetRole(x, y);
                if (role is TileRole.Finder or TileRole.Timing)
                    Assert.Equal(FrameFormat.IsStructuralBlack(x, y), Default.IsStructuralBlack(x, y));
            }
    }

    [Fact]
    public void Default_InterleaveMatchesFrameFormat()
    {
        for (int t = 0; t < FrameFormat.DataTileCount; t++)
            Assert.Equal(FrameFormat.ToCodewordSymbol(t), Default.ToCodewordSymbol(t));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(8)]
    public void Default_CodewordsForBitsMatchFrameFormat(int bitsPerTile)
    {
        Assert.Equal(FrameFormat.CodewordsForBits(bitsPerTile), Default.CodewordsForBits(bitsPerTile));
    }

    [Fact]
    public void Default_TileToPixelMatchesFrameFormat()
    {
        Assert.Equal(FrameFormat.TileToPixel(0, 0), Default.TileToPixel(0, 0));
        Assert.Equal(FrameFormat.TileToPixel(80.5, 45.5), Default.TileToPixel(80.5, 45.5));
    }

    [Theory]
    [InlineData(240, 135)]
    [InlineData(200, 120)]
    [InlineData(320, 180)]
    public void LargerGrid_IsInternallyConsistent(int w, int h)
    {
        var layout = new FrameLayout(w, h, 8);

        Assert.Equal(w * h, layout.TotalTiles);
        Assert.Equal(layout.CodewordCount * FrameFormat.CodewordLength, layout.DataTileCount);
        Assert.Equal(layout.DataTileCount, layout.DataTiles.Count);
        Assert.True(layout.CodewordCount > FrameFormat.CodewordCount, "A bigger grid should carry more codewords.");

        // Finders still sit at the four corners.
        Assert.Equal(TileRole.Finder, layout.GetRole(0, 0));
        Assert.Equal(TileRole.Finder, layout.GetRole(w - 1, h - 1));

        // Every data-tile index round-trips through the interleaver.
        Assert.All(Enumerable.Range(0, layout.DataTileCount), t =>
        {
            var (c, s) = layout.ToCodewordSymbol(t);
            Assert.Equal(t, layout.ToDataTileIndex(c, s));
        });
    }

    [Fact]
    public void TooSmallGrid_Throws()
    {
        Assert.Throws<ArgumentException>(() => new FrameLayout(40, 40, 8));
    }
}
