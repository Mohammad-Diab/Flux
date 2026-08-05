using FluxCore.Decoding;
using FluxCore.Ecc;
using FluxCore.Framing;
using FluxCore.Imaging;
using SkiaSharp;
using Xunit;

namespace FluxCore.Tests.Decoding;

public class FrameLocatorTests
{
    private static SKBitmap RenderMetadataFrame(uint totalFrames = 10)
    {
        var metadata = new MetadataPayload(
            sha256: Enumerable.Repeat((byte)0xAA, 32).ToArray(),
            payloadType: PayloadType.SevenZip,
            eccLevel: EccLevel.Medium,
            totalFrames: totalFrames,
            payloadLength: 400_000,
            originalName: "locator-test.7z",
            originalLength: 900_000,
            contentSignature: Enumerable.Repeat((byte)0xBB, 32).ToArray());
        var map = FrameEncoder.BuildMetadataFrame(metadata.Serialize(), totalFrames);
        return SKBitmap.Decode(FrameRenderer.RenderPng(map, ColorMap.Default));
    }

    private static SKBitmap Canvas(int width, int height, params (SKBitmap Frame, int X, int Y)[] placements)
    {
        var canvas = new SKBitmap(width, height);
        using var c = new SKCanvas(canvas);
        c.Clear(SKColors.White);
        foreach (var (frame, x, y) in placements)
            c.DrawBitmap(frame, x, y);
        return canvas;
    }

    [Fact]
    public void Locate_SingleBootstrapFrame_ReturnsOneRegionAtOffset()
    {
        var layout = BootstrapFrame.Layout;
        using var frame = RenderMetadataFrame();
        using var canvas = Canvas(2000, 1200, (frame, 300, 200));

        var region = Assert.Single(new FrameLocator(ColorMap.Default).Locate(canvas));

        Assert.InRange(region.X, 300 - layout.TilePixelSize, 300 + layout.TilePixelSize);
        Assert.InRange(region.Y, 200 - layout.TilePixelSize, 200 + layout.TilePixelSize);
        Assert.InRange(region.Width, layout.FrameWidthPx - 16, layout.FrameWidthPx + 16);
        Assert.InRange(region.Height, layout.FrameHeightPx - 16, layout.FrameHeightPx + 16);
    }

    [Fact]
    public void Locate_TwoBootstrapFrames_ReturnsBoth()
    {
        using var a = RenderMetadataFrame(totalFrames: 5);
        using var b = RenderMetadataFrame(totalFrames: 9);
        using var canvas = Canvas(3000, 1000, (a, 40, 60), (b, 1500, 120));

        var regions = new FrameLocator(ColorMap.Default).Locate(canvas);

        Assert.Equal(2, regions.Count);
        Assert.Contains(regions, r => Math.Abs(r.X - 40) <= 8 && Math.Abs(r.Y - 60) <= 8);
        Assert.Contains(regions, r => Math.Abs(r.X - 1500) <= 8 && Math.Abs(r.Y - 120) <= 8);
    }

    [Fact]
    public void Locate_NoFrame_ReturnsEmpty()
    {
        using var canvas = Canvas(1200, 800);
        Assert.Empty(new FrameLocator(ColorMap.Default).Locate(canvas));
    }

    [Fact]
    public void Locate_PayloadFrame_FoundWithItsAdoptedLayout()
    {
        var layout = new FrameLayout(240, 135, 8);
        var payload = new byte[4000];
        new Random(7).NextBytes(payload);
        var map = FrameEncoder.BuildFrame(5, 10, payload, EccLevel.Medium, 8, layout);
        using var frame = SKBitmap.Decode(FrameRenderer.RenderPng(map, ColorMap.Default));
        using var canvas = Canvas(2400, 1400, (frame, 150, 100));

        var locator = new FrameLocator(ColorMap.Default);
        Assert.Empty(locator.Locate(canvas));

        var region = Assert.Single(locator.Locate(canvas, [layout, BootstrapFrame.Layout]));
        Assert.Equal(5u, region.FrameId);
        Assert.InRange(region.X, 150 - layout.TilePixelSize, 150 + layout.TilePixelSize);
        Assert.InRange(region.Width, layout.FrameWidthPx - 16, layout.FrameWidthPx + 16);
    }
}
