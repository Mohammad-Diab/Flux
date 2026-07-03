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
                symbols[i] = map.GetPaletteValue(positions[i].X, positions[i].Y);
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
            codewords[c * FrameFormat.CodewordLength + s] = map.GetPaletteValue(x, y);
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
            codewords[c * FrameFormat.CodewordLength + s] = map.GetPaletteValue(x, y);
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
    public void BuildFrame_MetadataFrame_MustBeFrameZero()
    {
        var payload = DeterministicPayload(100);

        var map = FrameEncoder.BuildFrame(0, 5, payload, EccLevel.Max, isMetadataFrame: true);
        Assert.True(map.Header.IsMetadataFrame);

        Assert.Throws<ArgumentException>(() =>
            FrameEncoder.BuildFrame(1, 5, payload, EccLevel.Max, isMetadataFrame: true));
    }

    [Fact]
    public void BuildFrame_ThrowsWhenPayloadExceedsCapacity()
    {
        var payload = DeterministicPayload(EccLevel.Max.PayloadBytesPerFrame() + 1);

        Assert.Throws<ArgumentException>(() =>
            FrameEncoder.BuildFrame(0, 1, payload, EccLevel.Max));
    }

    [Fact]
    public void BuildFrame_RealMetadataPayload_RoundTrips()
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
            colorMap: FluxCore.Imaging.ColorMap.Default);
        var serialized = metadata.Serialize();

        var map = FrameEncoder.BuildFrame(0, 42, serialized, EccLevel.Max, isMetadataFrame: true);

        var codewords = new byte[ReedSolomonBlockCodec.EncodedFrameLength];
        for (int t = 0; t < FrameFormat.DataTileCount; t++)
        {
            var (c, s) = FrameFormat.ToCodewordSymbol(t);
            var (x, y) = FrameFormat.DataTiles[t];
            codewords[c * FrameFormat.CodewordLength + s] = map.GetPaletteValue(x, y);
        }

        var decoded = new byte[EccLevel.Max.PayloadBytesPerFrame()];
        Assert.True(ReedSolomonBlockCodec.TryDecodePayload(codewords, EccLevel.Max, decoded, out _));

        var restored = MetadataPayload.Deserialize(decoded[..map.Header.PayloadLength]);
        Assert.Equal("vacation-photos.7z", restored.OriginalName);
        Assert.Equal(42u, restored.TotalFrames);
        Assert.True(restored.MatchesFrameFormat());
    }
}
