using FluxCore.Decoding;
using FluxCore.Ecc;
using FluxCore.Framing;
using Xunit;

namespace FluxCore.Tests.Framing;

public class FrameCodecDepthTests
{
    private static byte[] Deterministic(int length, int seed = 7)
    {
        var data = new byte[length];
        new Random(seed).NextBytes(data);
        return data;
    }

    private static byte[] DataTileValues(FrameTileMap map)
    {
        var values = new byte[FrameFormat.DataTiles.Count];
        for (int t = 0; t < values.Length; t++)
        {
            var (x, y) = FrameFormat.DataTiles[t];
            values[t] = map.GetTileValue(x, y);
        }

        return values;
    }

    [Theory]
    [InlineData(8, 53)]
    [InlineData(6, 39)]
    [InlineData(4, 26)]
    [InlineData(3, 19)]
    public void CodewordsForBits_MatchesTileBudget(int bitsPerTile, int expected)
    {
        Assert.Equal(expected, FrameFormat.CodewordsForBits(bitsPerTile));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void PayloadTiles_RoundTrip_FullFrame(int bitsPerTile)
    {
        int codewords = FrameFormat.CodewordsForBits(bitsPerTile);
        var payload = Deterministic(EccLevel.Medium.PayloadBytesPerFrame(codewords));

        var map = FrameEncoder.BuildFrame(1, 10, payload, EccLevel.Medium, bitsPerTile);
        var values = DataTileValues(map);

        Assert.All(values, v => Assert.True(v < (1 << bitsPerTile)));
        Assert.True(FrameDecoder.TryDecodePayloadTiles(values, EccLevel.Medium, bitsPerTile, out var decoded, out _));
        Assert.Equal(payload, decoded);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(6)]
    public void PayloadTiles_RoundTrip_PartialPayload(int bitsPerTile)
    {
        int codewords = FrameFormat.CodewordsForBits(bitsPerTile);
        var payload = Deterministic(EccLevel.Medium.PayloadBytesPerFrame(codewords) / 3);

        var map = FrameEncoder.BuildFrame(2, 10, payload, EccLevel.Medium, bitsPerTile);
        var values = DataTileValues(map);

        Assert.True(FrameDecoder.TryDecodePayloadTiles(values, EccLevel.Medium, bitsPerTile, out var decoded, out _));
        Assert.Equal(payload, decoded[..payload.Length]);
    }

    [Fact]
    public void PayloadTiles_ScatteredErrors_AreCorrected()
    {
        const int bitsPerTile = 3;
        int codewords = FrameFormat.CodewordsForBits(bitsPerTile);
        var payload = Deterministic(EccLevel.Medium.PayloadBytesPerFrame(codewords));

        var map = FrameEncoder.BuildFrame(1, 10, payload, EccLevel.Medium, bitsPerTile);
        var values = DataTileValues(map);
        for (int i = 0; i < 15; i++)
            values[i * 37] ^= 0b101;

        Assert.True(FrameDecoder.TryDecodePayloadTiles(values, EccLevel.Medium, bitsPerTile, out var decoded, out int corrected));
        Assert.Equal(payload, decoded);
        Assert.True(corrected > 0);
    }

    [Fact]
    public void BuildFrame_DefaultDepth_MatchesExplicitEightBits()
    {
        var payload = Deterministic(EccLevel.Medium.PayloadBytesPerFrame());

        var implicitDefault = FrameEncoder.BuildFrame(3, 9, payload, EccLevel.Medium);
        var explicit8 = FrameEncoder.BuildFrame(3, 9, payload, EccLevel.Medium, bitsPerTile: 8);

        for (int y = 0; y < FrameFormat.GridHeightTiles; y++)
            for (int x = 0; x < FrameFormat.GridWidthTiles; x++)
                Assert.Equal(implicitDefault.GetTileValue(x, y), explicit8.GetTileValue(x, y));
    }
}
