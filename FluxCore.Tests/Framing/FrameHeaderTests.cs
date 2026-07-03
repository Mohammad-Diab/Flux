using FluxCore.Ecc;
using FluxCore.Framing;
using Xunit;

namespace FluxCore.Tests.Framing;

public class FrameHeaderTests
{
    [Fact]
    public void Constructor_SetsCurrentFormatVersion()
    {
        var header = new FrameHeader(1, 10, 5000, 0xDEADBEEF, EccLevel.Medium);

        Assert.Equal(FrameFormat.Version, header.FormatVersion);
        Assert.Equal(1u, header.FrameId);
        Assert.Equal(10u, header.TotalFrames);
        Assert.Equal(5000, header.PayloadLength);
        Assert.Equal(0xDEADBEEF, header.PayloadCrc32);
        Assert.Equal(EccLevel.Medium, header.EccLevel);
        Assert.False(header.IsMetadataFrame);
    }

    [Theory]
    [InlineData(EccLevel.Low, false)]
    [InlineData(EccLevel.Medium, false)]
    [InlineData(EccLevel.High, true)]
    [InlineData(EccLevel.Max, true)]
    public void SerializeDeserialize_RoundTrips_AllLevelsAndFlags(EccLevel level, bool isMetadata)
    {
        var original = new FrameHeader(
            frameId: isMetadata ? 0u : 42u,
            totalFrames: 317,
            payloadLength: (ushort)level.PayloadBytesPerFrame(),
            payloadCrc32: 0x12345678,
            eccLevel: level,
            isMetadataFrame: isMetadata);

        var bytes = original.Serialize();
        Assert.Equal(FrameHeader.Size, bytes.Length);

        var restored = FrameHeader.Deserialize(bytes);
        Assert.Equal(original, restored);
    }

    [Fact]
    public void SerializeDeserialize_RoundTrips_ExtremeValues()
    {
        var original = new FrameHeader(uint.MaxValue - 1, uint.MaxValue, ushort.MaxValue, uint.MaxValue, EccLevel.Max);

        var restored = FrameHeader.Deserialize(original.Serialize());

        Assert.Equal(original, restored);
    }

    [Fact]
    public void Serialize_ThrowsOnTooSmallDestination()
    {
        var header = new FrameHeader(0, 1, 0, 0, EccLevel.Medium, isMetadataFrame: true);

        Assert.Throws<ArgumentException>(() => header.Serialize(new byte[FrameHeader.Size - 1]));
    }

    [Fact]
    public void Deserialize_ThrowsOnTooSmallSource()
    {
        Assert.Throws<ArgumentException>(() => FrameHeader.Deserialize(new byte[FrameHeader.Size - 1]));
    }

    [Fact]
    public void IsPlausible_TrueForWellFormedHeader()
    {
        var header = new FrameHeader(5, 10, 10123, 0xABCD, EccLevel.Medium);

        Assert.True(header.IsPlausible());
    }

    [Fact]
    public void IsPlausible_FalseForWrongFormatVersion()
    {
        var header = FrameHeader.Deserialize(new byte[FrameHeader.Size]);

        Assert.False(header.IsPlausible());
    }

    [Fact]
    public void IsPlausible_FalseWhenFrameIdNotBelowTotalFrames()
    {
        var header = new FrameHeader(10, 10, 100, 0, EccLevel.Medium);

        Assert.False(header.IsPlausible());
    }

    [Fact]
    public void IsPlausible_FalseWhenPayloadExceedsLevelCapacity()
    {
        var header = new FrameHeader(1, 10, (ushort)(EccLevel.Max.PayloadBytesPerFrame() + 1), 0, EccLevel.Max);

        Assert.False(header.IsPlausible());
    }

    [Fact]
    public void IsPlausible_FalseWhenMetadataFrameIsNotFrameZero()
    {
        var header = new FrameHeader(1, 10, 100, 0, EccLevel.Max, isMetadataFrame: true);

        Assert.False(header.IsPlausible());
    }

    [Fact]
    public void Deserialize_CorruptedGarbage_IsNotPlausible()
    {
        var garbage = new byte[FrameHeader.Size];
        Array.Fill(garbage, (byte)0xFF);

        var header = FrameHeader.Deserialize(garbage);

        Assert.False(header.IsPlausible());
    }

    [Fact]
    public void EccLevelCapacities_MatchSpecification()
    {
        Assert.Equal(11819, EccLevel.Low.PayloadBytesPerFrame());
        Assert.Equal(10123, EccLevel.Medium.PayloadBytesPerFrame());
        Assert.Equal(8427, EccLevel.High.PayloadBytesPerFrame());
        Assert.Equal(6731, EccLevel.Max.PayloadBytesPerFrame());

        Assert.Equal(16, EccLevel.Low.CorrectableErrorsPerCodeword());
        Assert.Equal(32, EccLevel.Medium.CorrectableErrorsPerCodeword());
        Assert.Equal(48, EccLevel.High.CorrectableErrorsPerCodeword());
        Assert.Equal(64, EccLevel.Max.CorrectableErrorsPerCodeword());
    }
}
