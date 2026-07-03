using System.Security.Cryptography;
using FluxCore.Framing;
using Xunit;

namespace FluxCore.Tests.Framing;

public class FrameFormatTests
{
    [Fact]
    public void Constants_MatchSpecifiedGeometry()
    {
        Assert.Equal(160, FrameFormat.GridWidthTiles);
        Assert.Equal(90, FrameFormat.GridHeightTiles);
        Assert.Equal(14400, FrameFormat.TotalTiles);
        Assert.Equal(8, FrameFormat.TilePixelSize);
        Assert.Equal(16, FrameFormat.QuietZonePx);
        Assert.Equal(1312, FrameFormat.FrameWidthPx);
        Assert.Equal(752, FrameFormat.FrameHeightPx);
        Assert.Equal(13515, FrameFormat.DataTileCount);
        Assert.Equal(53 * 255, FrameFormat.DataTileCount);
    }

    [Fact]
    public void RoleCounts_MatchReservedTileAccounting()
    {
        var counts = new Dictionary<TileRole, int>();
        for (int y = 0; y < FrameFormat.GridHeightTiles; y++)
        {
            for (int x = 0; x < FrameFormat.GridWidthTiles; x++)
            {
                var role = FrameFormat.GetRole(x, y);
                counts[role] = counts.GetValueOrDefault(role) + 1;
            }
        }

        Assert.Equal(256, counts[TileRole.Finder]);
        Assert.Equal(218, counts[TileRole.Timing]);
        Assert.Equal(144, counts[TileRole.Header]);
        Assert.Equal(16, counts[TileRole.Beacon]);
        Assert.Equal(13515, counts[TileRole.Data]);
        Assert.Equal(251, counts[TileRole.Pad]);
        Assert.Equal(FrameFormat.TotalTiles, counts.Values.Sum());
    }

    [Fact]
    public void RoleMap_MatchesGoldenHash_FormatFreeze()
    {
        var roleBytes = new byte[FrameFormat.TotalTiles];
        for (int y = 0; y < FrameFormat.GridHeightTiles; y++)
        {
            for (int x = 0; x < FrameFormat.GridWidthTiles; x++)
            {
                roleBytes[y * FrameFormat.GridWidthTiles + x] = (byte)FrameFormat.GetRole(x, y);
            }
        }

        var hash = Convert.ToHexString(SHA256.HashData(roleBytes));

        Assert.Equal("64073E66463A5C746588F3616A88E28110219C509AC763C0DC22A1EB1DE5B01A", hash);
    }

    [Fact]
    public void DataTiles_AreInRowMajorScanOrder_AndRoleConsistent()
    {
        Assert.Equal(FrameFormat.DataTileCount, FrameFormat.DataTiles.Count);

        int previousScanIndex = -1;
        foreach (var (x, y) in FrameFormat.DataTiles)
        {
            int scanIndex = y * FrameFormat.GridWidthTiles + x;
            Assert.True(scanIndex > previousScanIndex, "Data tiles must be in row-major scan order.");
            previousScanIndex = scanIndex;
            Assert.Equal(TileRole.Data, FrameFormat.GetRole(x, y));
        }
    }

    [Fact]
    public void PadTiles_ComeAfterAllDataTiles_InScanOrder()
    {
        Assert.Equal(251, FrameFormat.PadTiles.Count);

        var lastData = FrameFormat.DataTiles[^1];
        int lastDataScanIndex = lastData.Y * FrameFormat.GridWidthTiles + lastData.X;

        foreach (var (x, y) in FrameFormat.PadTiles)
        {
            Assert.Equal(TileRole.Pad, FrameFormat.GetRole(x, y));
            Assert.True(y * FrameFormat.GridWidthTiles + x > lastDataScanIndex);
        }
    }

    [Fact]
    public void Interleaver_AnyConsecutive53DataTiles_Hit53DistinctCodewords()
    {
        for (int start = 0; start <= FrameFormat.DataTileCount - FrameFormat.CodewordCount; start += 97)
        {
            var codewords = new HashSet<int>();
            for (int t = start; t < start + FrameFormat.CodewordCount; t++)
            {
                codewords.Add(FrameFormat.ToCodewordSymbol(t).Codeword);
            }

            Assert.Equal(FrameFormat.CodewordCount, codewords.Count);
        }
    }

    [Fact]
    public void Interleaver_RoundTrips_ForAllDataTileIndices()
    {
        for (int t = 0; t < FrameFormat.DataTileCount; t++)
        {
            var (codeword, symbol) = FrameFormat.ToCodewordSymbol(t);
            Assert.InRange(codeword, 0, FrameFormat.CodewordCount - 1);
            Assert.InRange(symbol, 0, FrameFormat.CodewordLength - 1);
            Assert.Equal(t, FrameFormat.ToDataTileIndex(codeword, symbol));
        }
    }

    [Fact]
    public void HeaderCopies_HaveCorrectLength_NoOverlap_AndHeaderRole()
    {
        var seen = new HashSet<(int, int)>();
        for (int copy = 0; copy < FrameFormat.HeaderCopyCount; copy++)
        {
            var tiles = FrameFormat.GetHeaderCopyTiles(copy);
            Assert.Equal(FrameFormat.HeaderCopyLength, tiles.Count);

            foreach (var (x, y) in tiles)
            {
                Assert.Equal(TileRole.Header, FrameFormat.GetRole(x, y));
                Assert.True(seen.Add((x, y)), $"Header tile ({x},{y}) appears in more than one copy.");
            }
        }

        Assert.Equal(144, seen.Count);
    }

    [Fact]
    public void BeaconTiles_Are4x4Block_WithBeaconRole()
    {
        Assert.Equal(16, FrameFormat.BeaconTiles.Count);
        foreach (var (x, y) in FrameFormat.BeaconTiles)
        {
            Assert.InRange(x, 78, 81);
            Assert.InRange(y, 2, 5);
            Assert.Equal(TileRole.Beacon, FrameFormat.GetRole(x, y));
        }
    }

    [Fact]
    public void FinderPattern_HasQrProfile_OnCenterScanline()
    {
        var rowColors = new List<bool>();
        for (int x = 0; x < 7; x++)
        {
            rowColors.Add(FrameFormat.IsStructuralBlack(x, 3));
        }

        Assert.Equal([true, false, true, true, true, false, true], rowColors);

        Assert.True(FrameFormat.IsStructuralBlack(0, 0));
        Assert.False(FrameFormat.IsStructuralBlack(1, 1));
        Assert.True(FrameFormat.IsStructuralBlack(3, 3));
        Assert.False(FrameFormat.IsStructuralBlack(7, 0));
        Assert.False(FrameFormat.IsStructuralBlack(7, 7));
    }

    [Fact]
    public void TimingPattern_Alternates_StartingBlack()
    {
        Assert.True(FrameFormat.IsStructuralBlack(8, 0));
        Assert.False(FrameFormat.IsStructuralBlack(9, 0));
        Assert.True(FrameFormat.IsStructuralBlack(10, 0));

        Assert.True(FrameFormat.IsStructuralBlack(0, 8));
        Assert.False(FrameFormat.IsStructuralBlack(0, 9));
        Assert.True(FrameFormat.IsStructuralBlack(0, 10));
    }

    [Fact]
    public void FinderCenters_MapToExpectedPixelCoordinates()
    {
        Assert.Equal(4, FrameFormat.FinderCentersTiles.Count);

        var (px, py) = FrameFormat.TileToPixel(3.5, 3.5);
        Assert.Equal(44.0, px);
        Assert.Equal(44.0, py);

        var (px2, _) = FrameFormat.TileToPixel(156.5, 3.5);
        Assert.Equal(1268.0, px2);
    }

    [Fact]
    public void MetadataFrameTiles_CoverHeaderAndDataRoles_WithEnoughCapacity()
    {
        Assert.Equal(144 + 13515, FrameFormat.MetadataFrameTiles.Count);
        Assert.Equal(12 * 255, FrameFormat.MetadataEncodedBytes);
        Assert.Equal(12 * 127, FrameFormat.MetadataContentBytes);
        Assert.Equal(8160, FrameFormat.MetadataTilesUsed);
        Assert.True(FrameFormat.MetadataTilesUsed <= FrameFormat.MetadataFrameTiles.Count);

        foreach (var (x, y) in FrameFormat.MetadataFrameTiles)
        {
            var role = FrameFormat.GetRole(x, y);
            Assert.True(role is TileRole.Header or TileRole.Data);
        }
    }

    [Fact]
    public void MetadataFrameTiles_AreInRowMajorScanOrder()
    {
        int previous = -1;
        foreach (var (x, y) in FrameFormat.MetadataFrameTiles)
        {
            int scanIndex = y * FrameFormat.GridWidthTiles + x;
            Assert.True(scanIndex > previous);
            previous = scanIndex;
        }
    }

    [Fact]
    public void GetRole_ThrowsOnOutOfRangeCoordinates()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FrameFormat.GetRole(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => FrameFormat.GetRole(160, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => FrameFormat.GetRole(0, 90));
    }
}
