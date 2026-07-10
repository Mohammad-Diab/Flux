using FluxCore.Framing;
using Xunit;

namespace FluxCore.Tests.Framing;

public class TileBitPackerTests
{
    private static byte[] Deterministic(int length, int seed = 3)
    {
        var data = new byte[length];
        new Random(seed).NextBytes(data);
        return data;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void PackThenUnpack_RoundTrips(int bitsPerTile)
    {
        var data = Deterministic(200);

        var tiles = TileBitPacker.Pack(data, bitsPerTile);
        var restored = TileBitPacker.Unpack(tiles, bitsPerTile, data.Length);

        Assert.Equal(data, restored);
    }

    [Fact]
    public void Pack_EightBits_IsIdentity()
    {
        var data = Deterministic(64);
        Assert.Equal(data, TileBitPacker.Pack(data, 8));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(6)]
    public void Pack_TileValuesFitInDepth(int bitsPerTile)
    {
        var tiles = TileBitPacker.Pack(Deterministic(100), bitsPerTile);
        int max = (1 << bitsPerTile) - 1;
        Assert.All(tiles, v => Assert.True(v <= max));
    }

    [Fact]
    public void TileCount_MatchesCeilingOfBitsOverDepth()
    {
        // 127 bytes = 1016 bits; at 3 bits/tile that is ceil(1016/3) = 339 tiles.
        Assert.Equal(339, TileBitPacker.TileCount(127, 3));
        Assert.Equal(64, TileBitPacker.TileCount(64, 8));
        Assert.Equal(TileBitPacker.TileCount(100, 3), TileBitPacker.Pack(Deterministic(100), 3).Length);
    }

    [Fact]
    public void Pack_IsMsbFirst()
    {
        // 0b1011_0010, MSB-first in 3-bit groups -> 101 | 100 | 1(00 padded) = [5, 4, 4].
        var tiles = TileBitPacker.Pack([0b1011_0010], 3);
        Assert.Equal(new byte[] { 0b101, 0b100, 0b100 }, tiles);
    }

    [Fact]
    public void PackUnpack_EmptyIsEmpty()
    {
        Assert.Empty(TileBitPacker.Pack([], 4));
        Assert.Empty(TileBitPacker.Unpack([], 4, 0));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    public void InvalidDepth_Throws(int bitsPerTile)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TileBitPacker.Pack([1, 2, 3], bitsPerTile));
    }
}
