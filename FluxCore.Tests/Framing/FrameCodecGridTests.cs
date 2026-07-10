using FluxCore.Decoding;
using FluxCore.Ecc;
using FluxCore.Framing;
using FluxCore.Imaging;
using SkiaSharp;
using Xunit;

namespace FluxCore.Tests.Framing;

public class FrameCodecGridTests
{
    private static byte[] Deterministic(int length, int seed = 9)
    {
        var data = new byte[length];
        new Random(seed).NextBytes(data);
        return data;
    }

    private static byte[] DataTileValues(FrameTileMap map)
    {
        var layout = map.Layout;
        var values = new byte[layout.DataTiles.Count];
        for (int t = 0; t < values.Length; t++)
        {
            var (x, y) = layout.DataTiles[t];
            values[t] = map.GetTileValue(x, y);
        }

        return values;
    }

    public static IEnumerable<object[]> Grids =>
    [
        [96, 64],    // smaller than the default
        [160, 90],   // default
        [240, 135],  // larger
        [320, 180],  // larger still
    ];

    [Theory]
    [MemberData(nameof(Grids))]
    public void PayloadTiles_RoundTrip_AcrossGridsAndDepths(int w, int h)
    {
        var layout = new FrameLayout(w, h, 8);

        foreach (int bits in new[] { 3, 8 })
        {
            int codewords = layout.CodewordsForBits(bits);
            var payload = Deterministic(EccLevel.Medium.PayloadBytesPerFrame(codewords));

            var map = FrameEncoder.BuildFrame(1, 10, payload, EccLevel.Medium, bits, layout);
            var values = DataTileValues(map);

            Assert.True(
                FrameDecoder.TryDecodePayloadTiles(values, EccLevel.Medium, bits, out var decoded, out _, layout),
                $"{w}x{h} @ {bits}bpt failed to decode");
            Assert.Equal(payload, decoded);
        }
    }

    [Theory]
    [MemberData(nameof(Grids))]
    public void ImageRoundTrip_AcrossGrids(int w, int h)
    {
        var layout = new FrameLayout(w, h, 8);
        var payload = Deterministic(EccLevel.Medium.PayloadBytesPerFrame(layout.CodewordsForBits(8)));

        var map = FrameEncoder.BuildFrame(1, 5, payload, EccLevel.Medium, 8, layout);
        var png = FrameRenderer.RenderPng(map, ColorMap.Default);
        using var bitmap = SKBitmap.Decode(png);

        var result = new FrameDecoder(ColorMap.Default).Decode(bitmap, bitsPerTile: 8, layout: layout);

        Assert.True(result.Status == DecodeStatus.Success,
            $"{w}x{h}: {result.Status}/{result.FailureReason}; timing={result.Diagnostics.TimingMatchRatio:F3}; " +
            $"lowConf={result.Diagnostics.LowConfidenceDataTiles}; finders={result.Diagnostics.FinderPoints.Length}; " +
            $"img={bitmap.Width}x{bitmap.Height}");
        Assert.Equal(payload, result.Payload);
    }

    [Fact]
    public void SmallerGrid_CarriesFewerCodewords_LargerGridMore()
    {
        int small = new FrameLayout(96, 64, 8).CodewordsForBits(8);
        int mid = FrameLayout.Default.CodewordsForBits(8);
        int large = new FrameLayout(320, 180, 8).CodewordsForBits(8);

        Assert.True(small < mid, "96x64 should carry fewer codewords than 160x90.");
        Assert.True(large > 2 * mid, "320x180 (4x the tiles) should carry well over 2x the codewords.");
    }
}
