using FluxCore.Decoding;
using FluxCore.Ecc;
using FluxCore.Framing;
using FluxCore.Imaging;
using SkiaSharp;
using Xunit;

namespace FluxCore.Tests.Decoding;

public class FiducialDetectorTests
{
    private static SKBitmap RenderPristineFrame()
    {
        var random = new Random(7);
        var payload = new byte[3000];
        random.NextBytes(payload);
        var map = FrameEncoder.BuildFrame(1, 5, payload, EccLevel.Medium);
        return SKBitmap.Decode(FrameRenderer.RenderPng(map, ColorMap.Default));
    }

    private static SKBitmap Scale(SKBitmap source, double factor)
    {
        var info = new SKImageInfo(
            (int)(source.Width * factor), (int)(source.Height * factor), SKColorType.Rgba8888);
        return source.Resize(info, SKFilterQuality.Medium)!;
    }

    private static SKBitmap PadWithOffset(SKBitmap source, int offsetX, int offsetY)
    {
        var padded = new SKBitmap(source.Width + 120, source.Height + 120, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(padded);
        canvas.Clear(new SKColor(230, 230, 230));
        canvas.DrawBitmap(source, offsetX, offsetY);
        return padded;
    }

    private static void AssertNearCanonicalCorners(FinderPoint[] corners, double scale, double offsetX, double offsetY, double tolerance)
    {
        var expected = FrameFormat.FinderCentersTiles
            .Select(c => FrameFormat.TileToPixel(c.X, c.Y))
            .Select(p => (X: p.X * scale + offsetX, Y: p.Y * scale + offsetY))
            .ToArray();

        for (int i = 0; i < 4; i++)
        {
            Assert.True(Math.Abs(corners[i].X - expected[i].X) <= tolerance,
                $"Corner {i} X: expected ~{expected[i].X:F1}, got {corners[i].X:F1}");
            Assert.True(Math.Abs(corners[i].Y - expected[i].Y) <= tolerance,
                $"Corner {i} Y: expected ~{expected[i].Y:F1}, got {corners[i].Y:F1}");
        }
    }

    [Fact]
    public void TryDetect_PristineFrame_FindsFourCornersAccurately()
    {
        using var bitmap = RenderPristineFrame();

        var found = FiducialDetector.TryDetect(LumaImage.FromBitmap(bitmap), out var corners);

        Assert.True(found);
        AssertNearCanonicalCorners(corners, scale: 1, offsetX: 0, offsetY: 0, tolerance: 2);
    }

    [Theory]
    [InlineData(0.75)]
    [InlineData(1.5)]
    public void TryDetect_ScaledFrame_FindsFourCorners(double factor)
    {
        using var source = RenderPristineFrame();
        using var scaled = Scale(source, factor);

        var found = FiducialDetector.TryDetect(LumaImage.FromBitmap(scaled), out var corners);

        Assert.True(found);
        AssertNearCanonicalCorners(corners, scale: factor, offsetX: 0, offsetY: 0, tolerance: 3);
    }

    [Fact]
    public void TryDetect_OffsetFrameOnGrayBackground_FindsFourCorners()
    {
        using var source = RenderPristineFrame();
        using var padded = PadWithOffset(source, 30, 45);

        var found = FiducialDetector.TryDetect(LumaImage.FromBitmap(padded), out var corners);

        Assert.True(found);
        AssertNearCanonicalCorners(corners, scale: 1, offsetX: 30, offsetY: 45, tolerance: 3);
    }

    [Fact]
    public void TryDetect_BlankImage_ReturnsFalse()
    {
        using var blank = new SKBitmap(400, 300, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using (var canvas = new SKCanvas(blank))
        {
            canvas.Clear(SKColors.White);
        }

        var found = FiducialDetector.TryDetect(LumaImage.FromBitmap(blank), out _);

        Assert.False(found);
    }

    [Fact]
    public void TryDetect_ModuleSize_ReflectsScale()
    {
        using var source = RenderPristineFrame();
        using var scaled = Scale(source, 1.5);

        Assert.True(FiducialDetector.TryDetect(LumaImage.FromBitmap(scaled), out var corners));

        foreach (var corner in corners)
        {
            Assert.InRange(corner.ModuleSize, 9, 15);
        }
    }
}
