using FluxCore.Decoding;
using FluxCore.Ecc;
using FluxCore.Framing;
using FluxCore.Imaging;
using SkiaSharp;
using Xunit;

namespace FluxCore.Tests.Integration;

/// <summary>
/// Full render→decode round-trips at higher colour depths on a clean (pixel-perfect) channel:
/// the encoder renders at the N-colour palette and the decoder adopts it. 512 is as robust as 256;
/// 1024 is clear-channel only, which an exact PNG round-trip satisfies.
/// </summary>
public class ColorDepthRoundTripTests
{
    private static byte[] Deterministic(int length, int seed = 11)
    {
        var data = new byte[length];
        new Random(seed).NextBytes(data);
        return data;
    }

    [Theory]
    [InlineData(256)]
    [InlineData(512)]
    [InlineData(1024)]
    public void FullFrame_RoundTrips_AtColorDepth(int colorCount)
    {
        int bits = PaletteGenerator.BitsForCount(colorCount);
        var colorMap = ColorMap.FromCount(colorCount);
        var layout = FrameLayout.Default;
        var payload = Deterministic(EccLevel.Medium.PayloadBytesPerFrame(layout.CodewordsForBits(bits)));

        var map = FrameEncoder.BuildFrame(1, 3, payload, EccLevel.Medium, bits, layout);
        var png = FrameRenderer.RenderPng(map, colorMap);
        using var bitmap = SKBitmap.Decode(png);

        var decoder = new FrameDecoder(colorMap);
        var result = decoder.Decode(bitmap, bitsPerTile: bits, layout: layout);

        Assert.Equal(DecodeStatus.Success, result.Status);
        Assert.Equal(payload, result.Payload);
    }

    [Theory]
    [InlineData(512)]
    [InlineData(1024)]
    public void HigherDepth_CarriesMorePerFrame_ThanDefault(int colorCount)
    {
        var layout = FrameLayout.Default;
        int baseline = EccLevel.Medium.PayloadBytesPerFrame(layout.CodewordsForBits(8));
        int denser = EccLevel.Medium.PayloadBytesPerFrame(
            layout.CodewordsForBits(PaletteGenerator.BitsForCount(colorCount)));

        Assert.True(denser > baseline, $"{colorCount} colours must carry more than 256 ({denser} vs {baseline}).");
    }
}
