using FluxCore.Framing;
using FluxCore.Imaging;
using Xunit;

namespace FluxCore.Tests.Framing;

public class BootstrapFrameTests
{
    [Fact]
    public void Layout_PinsTheBootstrapGeometry()
    {
        var layout = BootstrapFrame.Layout;

        Assert.Equal(96, layout.GridWidthTiles);
        Assert.Equal(54, layout.GridHeightTiles);
        Assert.Equal(8, layout.TilePixelSize);
        Assert.Equal(1, layout.BitsPerTile);
        Assert.Equal(800, layout.FrameWidthPx);
        Assert.Equal(464, layout.FrameHeightPx);
    }

    [Fact]
    public void Codec_PinsTwoHalfRateCodewords()
    {
        Assert.Equal(2, BootstrapFrame.CodewordCount);
        Assert.Equal(2 * 127, BootstrapFrame.ContentBytes);
        Assert.Equal(2 * 255, BootstrapFrame.EncodedBytes);
        Assert.Equal(128, BootstrapFrame.ParitySymbols);
        Assert.Equal(4080, BootstrapFrame.TilesUsed);
    }

    [Fact]
    public void MetadataFrameTiles_CoverHeaderAndDataRoles_WithEnoughCapacity()
    {
        Assert.True(BootstrapFrame.TilesUsed <= BootstrapFrame.MetadataFrameTiles.Count);

        foreach (var (x, y) in BootstrapFrame.MetadataFrameTiles)
        {
            var role = BootstrapFrame.Layout.GetRole(x, y);
            Assert.True(role is TileRole.Header or TileRole.Data);
        }
    }

    [Fact]
    public void MetadataFrameTiles_AreInRowMajorScanOrder()
    {
        int previous = -1;
        foreach (var (x, y) in BootstrapFrame.MetadataFrameTiles)
        {
            int scanIndex = y * BootstrapFrame.Layout.GridWidthTiles + x;
            Assert.True(scanIndex > previous);
            previous = scanIndex;
        }
    }

    [Fact]
    public void MetadataCapacity_FitsTheFixedFieldsAndAMaxLengthName()
    {
        Assert.True(MetadataPayload.FixedSize + MetadataPayload.MaxNameBytes == BootstrapFrame.ContentBytes);
        Assert.True(MetadataPayload.MaxNameBytes >= 128);
    }

    [Fact]
    public void MonoColors_ClassifyAndToColor_RoundTrip()
    {
        Assert.Equal(new Rgb24(0, 0, 0), MonoColors.ToColor(0));
        Assert.Equal(new Rgb24(255, 255, 255), MonoColors.ToColor(1));
        Assert.Equal(0, MonoColors.Classify(40, 127.5));
        Assert.Equal(1, MonoColors.Classify(210, 127.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => MonoColors.ToColor(2));
    }
}
