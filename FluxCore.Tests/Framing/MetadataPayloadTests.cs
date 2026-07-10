using FluxCore.Ecc;
using FluxCore.Framing;
using FluxCore.Imaging;
using Xunit;

namespace FluxCore.Tests.Framing;

public class MetadataPayloadTests
{
    private static byte[] Filled(byte value) =>
        Enumerable.Repeat(value, 32).ToArray();

    private static MetadataPayload CreateValid(
        string name = "document.pdf",
        EccLevel level = EccLevel.Medium,
        PayloadType payloadType = PayloadType.SevenZip) =>
        new(
            sha256: Filled(0xAA),
            payloadType: payloadType,
            eccLevel: level,
            totalFrames: 42,
            payloadLength: 400_000,
            originalName: name,
            originalLength: 1_200_000,
            contentSignature: Filled(0xBB),
            colorCount: 256);

    [Fact]
    public void Constructor_SetsCurrentVersionAndFrameFormatGeometry()
    {
        var payload = CreateValid();

        Assert.Equal(MetadataPayload.CurrentVersion, payload.Version);
        Assert.Equal(FrameFormat.TilePixelSize, payload.TilePixelSize);
        Assert.Equal(FrameFormat.GridWidthTiles, payload.GridWidthTiles);
        Assert.Equal(FrameFormat.GridHeightTiles, payload.GridHeightTiles);
        Assert.True(payload.TryBuildLayout(out _));
    }

    [Fact]
    public void Constructor_ThrowsOnInvalidArguments()
    {
        Assert.Throws<ArgumentException>(() => new MetadataPayload(
            new byte[31], PayloadType.Raw, EccLevel.Low, 1, 0, "x", 0, Filled(0), 256));
        Assert.Throws<ArgumentException>(() => new MetadataPayload(
            Filled(0), PayloadType.Raw, EccLevel.Low, 1, 0, "x", 0, new byte[16], 256));
        Assert.Throws<ArgumentException>(() => new MetadataPayload(
            Filled(0), PayloadType.Raw, (EccLevel)9, 1, 0, "x", 0, Filled(0), 256));
        Assert.Throws<ArgumentException>(() => new MetadataPayload(
            Filled(0), PayloadType.Raw, EccLevel.Low, 0, 0, "x", 0, Filled(0), 256));
        Assert.Throws<ArgumentException>(() => new MetadataPayload(
            Filled(0), PayloadType.Raw, EccLevel.Low, 1, -1, "x", 0, Filled(0), 256));
        Assert.Throws<ArgumentNullException>(() => new MetadataPayload(
            Filled(0), PayloadType.Raw, EccLevel.Low, 1, 0, null!, 0, Filled(0), 256));
    }

    [Theory]
    [InlineData(EccLevel.Low, PayloadType.Raw)]
    [InlineData(EccLevel.Medium, PayloadType.SevenZip)]
    [InlineData(EccLevel.Max, PayloadType.SevenZip)]
    public void SerializeDeserialize_RoundTrips(EccLevel level, PayloadType payloadType)
    {
        var original = CreateValid(level: level, payloadType: payloadType);

        var restored = MetadataPayload.Deserialize(original.Serialize());

        Assert.Equal(original.Version, restored.Version);
        Assert.Equal(original.Sha256, restored.Sha256);
        Assert.Equal(original.PayloadType, restored.PayloadType);
        Assert.Equal(original.EccLevel, restored.EccLevel);
        Assert.Equal(original.TilePixelSize, restored.TilePixelSize);
        Assert.Equal(original.GridWidthTiles, restored.GridWidthTiles);
        Assert.Equal(original.GridHeightTiles, restored.GridHeightTiles);
        Assert.Equal(original.TotalFrames, restored.TotalFrames);
        Assert.Equal(original.PayloadLength, restored.PayloadLength);
        Assert.Equal(original.OriginalName, restored.OriginalName);
        Assert.Equal(original.OriginalLength, restored.OriginalLength);
        Assert.Equal(original.ContentSignature, restored.ContentSignature);
        Assert.Equal(original.ColorCount, restored.ColorCount);
        Assert.True(restored.TryBuildLayout(out _));
    }

    [Fact]
    public void SerializeDeserialize_PreservesColorCount()
    {
        var original = new MetadataPayload(
            Filled(0xAA), PayloadType.Raw, EccLevel.Medium, 5, 1000, "x", 2000, Filled(0xBB), colorCount: 64);

        Assert.Equal(64, MetadataPayload.Deserialize(original.Serialize()).ColorCount);
        Assert.Equal(256, MetadataPayload.Deserialize(CreateValid().Serialize()).ColorCount);
    }

    [Fact]
    public void Constructor_ThrowsOnUnsupportedColorCount()
    {
        Assert.Throws<ArgumentException>(() => new MetadataPayload(
            Filled(0), PayloadType.Raw, EccLevel.Low, 1, 0, "x", 0, Filled(0), colorCount: 100));
    }

    [Theory]
    [InlineData("")]
    [InlineData("simple.txt")]
    [InlineData("Ω-мир-日本語-🎨.7z")]
    public void SerializeDeserialize_HandlesUnicodeAndEmptyNames(string name)
    {
        var original = CreateValid(name: name);

        var restored = MetadataPayload.Deserialize(original.Serialize());

        Assert.Equal(name, restored.OriginalName);
    }

    [Fact]
    public void SerializeDeserialize_HandlesLongName()
    {
        var name = new string('x', 1000);
        var original = CreateValid(name: name);

        var restored = MetadataPayload.Deserialize(original.Serialize());

        Assert.Equal(name, restored.OriginalName);
    }

    [Fact]
    public void Serialize_SizeIsFixedSizePlusNameBytes()
    {
        var payload = CreateValid(name: "abc");

        Assert.Equal(MetadataPayload.FixedSize + 3, payload.Serialize().Length);
    }

    [Fact]
    public void Serialize_FitsInMaxLevelFrameCapacity_WithGenerousName()
    {
        var payload = CreateValid(name: new string('n', 255));

        Assert.True(payload.Serialize().Length <= EccLevel.Max.PayloadBytesPerFrame(),
            "Frame 0 must always fit in a Max-level frame.");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Deserialize_RejectsOlderVersions(byte version)
    {
        var data = new byte[MetadataPayload.FixedSize];
        data[0] = version;

        Assert.Throws<NotSupportedException>(() => MetadataPayload.Deserialize(data));
    }

    [Fact]
    public void Deserialize_RejectsTruncatedData()
    {
        var full = CreateValid().Serialize();

        Assert.Throws<ArgumentException>(() => MetadataPayload.Deserialize(full[..(full.Length - 100)]));
        Assert.Throws<ArgumentException>(() => MetadataPayload.Deserialize(new byte[10]));
    }

    [Fact]
    public void TryBuildLayout_AdoptsNonDefaultGrid()
    {
        var payload = new MetadataPayload(
            Filled(0xAA), PayloadType.SevenZip, EccLevel.Medium, 42, 400_000, "d.pdf", 1_200_000, Filled(0xBB), 256)
        {
            GridWidthTiles = 240,
            GridHeightTiles = 135,
        };

        Assert.True(payload.TryBuildLayout(out var layout));
        Assert.Equal(240, layout!.GridWidthTiles);
        Assert.Equal(135, layout.GridHeightTiles);
        Assert.Equal(FrameFormat.TilePixelSize, layout.TilePixelSize);
    }

    [Fact]
    public void TryBuildLayout_RejectsUnconstructibleGrid()
    {
        var payload = new MetadataPayload(
            Filled(0xAA), PayloadType.SevenZip, EccLevel.Medium, 42, 400_000, "d.pdf", 1_200_000, Filled(0xBB), 256)
        {
            GridWidthTiles = 10,
            GridHeightTiles = 10,
        };

        Assert.False(payload.TryBuildLayout(out _));
    }

    [Fact]
    public void TryBuildLayout_RejectsPayloadOverflowingHeaderField()
    {
        // 400×200 @ Low exceeds the 65,535-byte ushort PayloadLength per frame.
        var payload = new MetadataPayload(
            Filled(0xAA), PayloadType.SevenZip, EccLevel.Low, 42, 400_000, "d.pdf", 1_200_000, Filled(0xBB), 256)
        {
            GridWidthTiles = 400,
            GridHeightTiles = 200,
        };

        Assert.False(payload.TryBuildLayout(out _));
    }
}
