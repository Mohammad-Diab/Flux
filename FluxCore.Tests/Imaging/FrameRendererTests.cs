using FluxCore.Ecc;
using FluxCore.Framing;
using FluxCore.Imaging;
using SkiaSharp;
using Xunit;

namespace FluxCore.Tests.Imaging;

public class FrameRendererTests
{
    private static readonly SKColor White = new(255, 255, 255);
    private static readonly SKColor Black = new(0, 0, 0);

    private static byte[] DeterministicPayload(int length, int seed = 99)
    {
        var random = new Random(seed);
        var payload = new byte[length];
        random.NextBytes(payload);
        return payload;
    }

    private static SKColor CenterPixel(SKBitmap bitmap, int tileX, int tileY)
    {
        var (px, py) = FrameFormat.TileToPixel(tileX + 0.5, tileY + 0.5);
        return bitmap.GetPixel((int)px, (int)py);
    }

    private static SKBitmap Render(uint frameId = 2, int payloadLength = 4000)
    {
        var map = FrameEncoder.BuildFrame(
            frameId, 10, DeterministicPayload(payloadLength), EccLevel.Medium);
        var png = FrameRenderer.RenderPng(map, ColorMap.Default);
        return SKBitmap.Decode(png);
    }

    [Fact]
    public void RenderPng_ProducesCanonicalDimensions()
    {
        using var bitmap = Render();

        Assert.Equal(1312, bitmap.Width);
        Assert.Equal(752, bitmap.Height);
    }

    [Fact]
    public void RenderPng_QuietZoneIsWhite()
    {
        using var bitmap = Render();

        Assert.Equal(White, bitmap.GetPixel(5, 5));
        Assert.Equal(White, bitmap.GetPixel(1306, 5));
        Assert.Equal(White, bitmap.GetPixel(5, 746));
        Assert.Equal(White, bitmap.GetPixel(1306, 746));
    }

    [Fact]
    public void RenderPng_FinderPattern_HasExactBlackWhiteProfile()
    {
        using var bitmap = Render();

        Assert.Equal(Black, CenterPixel(bitmap, 0, 0));
        Assert.Equal(White, CenterPixel(bitmap, 1, 1));
        Assert.Equal(Black, CenterPixel(bitmap, 3, 3));
        Assert.Equal(White, CenterPixel(bitmap, 7, 7));
        Assert.Equal(Black, CenterPixel(bitmap, 156, 3));
        Assert.Equal(Black, CenterPixel(bitmap, 3, 86));
        Assert.Equal(Black, CenterPixel(bitmap, 156, 86));
    }

    [Fact]
    public void RenderPng_TimingPattern_Alternates()
    {
        using var bitmap = Render();

        Assert.Equal(Black, CenterPixel(bitmap, 8, 0));
        Assert.Equal(White, CenterPixel(bitmap, 9, 0));
        Assert.Equal(Black, CenterPixel(bitmap, 0, 8));
        Assert.Equal(White, CenterPixel(bitmap, 0, 9));
    }

    [Theory]
    [InlineData(2u, true)]
    [InlineData(3u, false)]
    public void RenderPng_Beacon_FollowsFrameParity(uint frameId, bool expectBlack)
    {
        using var bitmap = Render(frameId: frameId);

        var expected = expectBlack ? Black : White;
        Assert.Equal(expected, CenterPixel(bitmap, 78, 2));
        Assert.Equal(expected, CenterPixel(bitmap, 81, 5));
    }

    [Fact]
    public void RenderPng_DataAndHeaderTiles_MatchPaletteExactly()
    {
        var map = FrameEncoder.BuildFrame(2, 10, DeterministicPayload(4000), EccLevel.Medium);
        var png = FrameRenderer.RenderPng(map, ColorMap.Default);
        using var bitmap = SKBitmap.Decode(png);

        var samples = FrameFormat.DataTiles.Take(500)
            .Concat(FrameFormat.GetHeaderCopyTiles(0))
            .Concat(FrameFormat.GetHeaderCopyTiles(1))
            .Concat(FrameFormat.GetHeaderCopyTiles(2));

        foreach (var (x, y) in samples)
        {
            var expected = ColorMap.Default.GetColor(map.GetPaletteValue(x, y));
            var actual = CenterPixel(bitmap, x, y);
            Assert.Equal(new SKColor(expected.R, expected.G, expected.B), actual);
        }
    }

    [Fact]
    public void RenderPng_PadTilesAreWhite()
    {
        using var bitmap = Render();

        foreach (var (x, y) in FrameFormat.PadTiles)
        {
            Assert.Equal(White, CenterPixel(bitmap, x, y));
        }
    }

    [Fact]
    public void RenderPng_MetadataFrameZero_Renders()
    {
        var metadata = new MetadataPayload(
            sha256: DeterministicPayload(32),
            payloadType: PayloadType.SevenZip,
            eccLevel: EccLevel.Medium,
            totalFrames: 5,
            payloadLength: 40_000,
            originalName: "notes.7z",
            originalLength: 100_000,
            contentSignature: DeterministicPayload(32, seed: 3),
            colorMap: ColorMap.Default);

        var map = FrameEncoder.BuildFrame(0, 5, metadata.Serialize(), EccLevel.Max, isMetadataFrame: true);
        var png = FrameRenderer.RenderPng(map, ColorMap.Default);

        using var bitmap = SKBitmap.Decode(png);
        Assert.Equal(1312, bitmap.Width);
        Assert.Equal(Black, CenterPixel(bitmap, 78, 2));
    }

    [Fact]
    public void RenderPng_IsDeterministic()
    {
        var map = FrameEncoder.BuildFrame(1, 3, DeterministicPayload(500), EccLevel.High);

        var first = FrameRenderer.RenderPng(map, ColorMap.Default);
        var second = FrameRenderer.RenderPng(map, ColorMap.Default);

        Assert.Equal(first, second);
    }
}
