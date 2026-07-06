using FluxCore.Decoding;
using FluxCore.Ecc;
using FluxCore.Framing;
using FluxCore.Imaging;
using SkiaSharp;
using Xunit;

namespace FluxCore.Tests.Decoding;

public class FrameLocatorTests
{
    private static SKBitmap RenderFrame(uint frameId = 5, uint total = 10)
    {
        var payload = new byte[4000];
        new Random(7).NextBytes(payload);
        var map = FrameEncoder.BuildFrame(frameId, total, payload, EccLevel.Medium);
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
    public void Locate_SingleFrame_ReturnsOneRegionAtOffset()
    {
        using var frame = RenderFrame(frameId: 5);
        using var canvas = Canvas(2000, 1200, (frame, 300, 200));

        var region = Assert.Single(new FrameLocator(ColorMap.Default).Locate(canvas));

        Assert.Equal(5u, region.FrameId);
        Assert.InRange(region.X, 300 - FrameFormat.TilePixelSize, 300 + FrameFormat.TilePixelSize);
        Assert.InRange(region.Y, 200 - FrameFormat.TilePixelSize, 200 + FrameFormat.TilePixelSize);
        Assert.InRange(region.Width, FrameFormat.FrameWidthPx - 16, FrameFormat.FrameWidthPx + 16);
        Assert.InRange(region.Height, FrameFormat.FrameHeightPx - 16, FrameFormat.FrameHeightPx + 16);
    }

    [Fact]
    public void Locate_TwoFrames_ReturnsBoth()
    {
        using var a = RenderFrame(frameId: 2);
        using var b = RenderFrame(frameId: 7);
        using var canvas = Canvas(3000, 1000, (a, 40, 60), (b, 1500, 120));

        var regions = new FrameLocator(ColorMap.Default).Locate(canvas);

        Assert.Equal(2, regions.Count);
        Assert.Contains(regions, r => r.FrameId == 2u);
        Assert.Contains(regions, r => r.FrameId == 7u);
    }

    [Fact]
    public void Locate_NoFrame_ReturnsEmpty()
    {
        using var canvas = Canvas(1200, 800);
        Assert.Empty(new FrameLocator(ColorMap.Default).Locate(canvas));
    }
}
