using FluxCore.Ecc;

using FluxCore.Framing;
using FluxCore.Hashing;
using Xunit;

namespace FluxCore.Tests.Framing;

public class FrameEncoderTests
{
    private static byte[] DeterministicPayload(int length, int seed = 77)
    {
        var random = new Random(seed);
        var payload = new byte[length];
        random.NextBytes(payload);
        return payload;
    }

    [Fact]
    public void BuildFrame_HeaderCarriesPayloadLengthCrcAndLevel()
    {
        var payload = DeterministicPayload(5000);

        var map = FrameEncoder.BuildFrame(3, 10, payload, EccLevel.Medium);

        Assert.Equal(3u, map.Header.FrameId);
        Assert.Equal(10u, map.Header.TotalFrames);
        Assert.Equal(5000, map.Header.PayloadLength);
        Assert.Equal(Crc32Helper.ComputeChecksum(payload), map.Header.PayloadCrc32);
        Assert.Equal(EccLevel.Medium, map.Header.EccLevel);
        Assert.True(map.Header.IsPlausible());
    }

    [Fact]
    public void BuildFrame_HeaderCopies_DecodeBackToSameHeader()
    {
        var payload = DeterministicPayload(1000);
        var map = FrameEncoder.BuildFrame(7, 20, payload, EccLevel.High);

        for (int copy = 0; copy < FrameFormat.HeaderCopyCount; copy++)
        {
            var symbols = new byte[FrameFormat.HeaderCopyLength];
            var positions = FrameFormat.GetHeaderCopyTiles(copy);
            for (int i = 0; i < symbols.Length; i++)
            {
                symbols[i] = map.GetTileValue(positions[i].X, positions[i].Y);
            }

            Assert.True(ReedSolomonBlockCodec.TryDecodeHeader(symbols, out var decoded));
            Assert.Equal(map.Header, decoded);
        }
    }

    [Theory]
    [InlineData(EccLevel.Low)]
    [InlineData(EccLevel.Medium)]
    [InlineData(EccLevel.Max)]
    public void BuildFrame_DataTiles_DecodeBackToPayload(EccLevel level)
    {
        var payload = DeterministicPayload(level.PayloadBytesPerFrame());
        var map = FrameEncoder.BuildFrame(1, 2, payload, level);

        var codewords = new byte[ReedSolomonBlockCodec.EncodedFrameLength];
        for (int t = 0; t < FrameFormat.DataTileCount; t++)
        {
            var (c, s) = FrameFormat.ToCodewordSymbol(t);
            var (x, y) = FrameFormat.DataTiles[t];
            codewords[c * FrameFormat.CodewordLength + s] = map.GetTileValue(x, y);
        }

        var decoded = new byte[level.PayloadBytesPerFrame()];
        Assert.True(ReedSolomonBlockCodec.TryDecodePayload(codewords, level, decoded, out _));
        Assert.Equal(payload, decoded);
    }

    [Fact]
    public void BuildFrame_PartialLastFramePayload_LengthHonored_RestZeroPadded()
    {
        var payload = DeterministicPayload(1234);
        var map = FrameEncoder.BuildFrame(9, 10, payload, EccLevel.Medium);

        Assert.Equal(1234, map.Header.PayloadLength);

        var codewords = new byte[ReedSolomonBlockCodec.EncodedFrameLength];
        for (int t = 0; t < FrameFormat.DataTileCount; t++)
        {
            var (c, s) = FrameFormat.ToCodewordSymbol(t);
            var (x, y) = FrameFormat.DataTiles[t];
            codewords[c * FrameFormat.CodewordLength + s] = map.GetTileValue(x, y);
        }

        var decoded = new byte[EccLevel.Medium.PayloadBytesPerFrame()];
        Assert.True(ReedSolomonBlockCodec.TryDecodePayload(codewords, EccLevel.Medium, decoded, out _));
        Assert.Equal(payload, decoded[..1234]);
        Assert.All(decoded[1234..], b => Assert.Equal(0, b));
        Assert.True(Crc32Helper.Verify(decoded.AsSpan(0, map.Header.PayloadLength), map.Header.PayloadCrc32));
    }

    [Theory]
    [InlineData(0u, true)]
    [InlineData(1u, false)]
    [InlineData(2u, true)]
    [InlineData(317u, false)]
    public void BuildFrame_BeaconParity_FollowsFrameId(uint frameId, bool expectBlack)
    {
        var map = FrameEncoder.BuildFrame(frameId, 1000, DeterministicPayload(10), EccLevel.Medium);

        Assert.Equal(expectBlack, map.BeaconIsBlack);
    }

    [Fact]
    public void BuildFrame_ThrowsWhenPayloadExceedsCapacity()
    {
        var payload = DeterministicPayload(EccLevel.Max.PayloadBytesPerFrame() + 1);

        Assert.Throws<ArgumentException>(() =>
            FrameEncoder.BuildFrame(1, 2, payload, EccLevel.Max));
    }

    [Fact]
    public void BuildMetadataFrame_IsCubeCornerScheme_WithBlackBeacon()
    {
        var map = FrameEncoder.BuildMetadataFrame(DeterministicPayload(500), 42);

        Assert.Equal(TileColorScheme.CubeCorner8, map.ColorScheme);
        Assert.True(map.Header.IsMetadataFrame);
        Assert.Equal(0u, map.Header.FrameId);
        Assert.True(map.BeaconIsBlack);

        foreach (var (x, y) in FrameFormat.MetadataFrameTiles.Take(FrameFormat.MetadataTilesUsed))
        {
            Assert.InRange(map.GetTileValue(x, y), (byte)0, (byte)7);
        }
    }

    [Fact]
    public void BuildMetadataFrame_ThrowsWhenContentExceedsCapacity()
    {
        var content = DeterministicPayload(FrameFormat.MetadataContentBytes + 1);

        Assert.Throws<ArgumentException>(() => FrameEncoder.BuildMetadataFrame(content, 2));
    }

    [Fact]
    public void BuildMetadataFrame_RealMetadataPayload_DecodesBack()
    {
        var metadata = new MetadataPayload(
            sha256: DeterministicPayload(32),
            payloadType: PayloadType.SevenZip,
            eccLevel: EccLevel.Medium,
            totalFrames: 42,
            payloadLength: 400_000,
            originalName: "vacation-photos.7z",
            originalLength: 900_000,
            contentSignature: DeterministicPayload(32, seed: 5),
            colorCount: 256);

        var map = FrameEncoder.BuildMetadataFrame(metadata.Serialize(), 42);

        var stream = new byte[FrameFormat.MetadataEncodedBytes];
        var positions = FrameFormat.MetadataFrameTiles;
        for (int t = 0; t < FrameFormat.MetadataTilesUsed; t++)
        {
            var (x, y) = positions[t];
            int value = map.GetTileValue(x, y);
            for (int k = 0; k < 3; k++)
            {
                int bit = (value >> (2 - k)) & 1;
                int globalBit = t * 3 + k;
                if (bit != 0)
                    stream[globalBit >> 3] |= (byte)(1 << (7 - (globalBit & 7)));
            }
        }

        var content = new byte[FrameFormat.MetadataContentBytes];
        int parity = FrameFormat.CodewordLength - FrameFormat.MetadataCodewordDataBytes;
        for (int c = 0; c < FrameFormat.MetadataCodewordCount; c++)
        {
            var block = new byte[FrameFormat.CodewordLength];
            for (int s = 0; s < FrameFormat.CodewordLength; s++)
            {
                block[s] = stream[s * FrameFormat.MetadataCodewordCount + c];
            }

            Assert.True(ReedSolomonBlockCodec.TryDecodeBlock(
                block, parity, content.AsSpan(c * FrameFormat.MetadataCodewordDataBytes, FrameFormat.MetadataCodewordDataBytes), out _));
        }

        var restored = MetadataPayload.Deserialize(content);
        Assert.Equal("vacation-photos.7z", restored.OriginalName);
        Assert.Equal(42u, restored.TotalFrames);
        Assert.True(restored.MatchesFrameFormat());
    }
}
