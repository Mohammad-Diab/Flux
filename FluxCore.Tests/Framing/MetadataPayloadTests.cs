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
        Assert.Equal(original.PaletteKind, restored.PaletteKind);
        Assert.True(restored.TryBuildLayout(out _));
    }

    [Fact]
    public void SerializeDeserialize_PreservesPaletteKind()
    {
        var standard = CreateValid();
        Assert.Equal(PaletteKind.Standard, MetadataPayload.Deserialize(standard.Serialize()).PaletteKind);

        var rugged = new MetadataPayload(
            Filled(0xAA), PayloadType.Raw, EccLevel.Medium, 5, 1000, "x", 2000, Filled(0xBB),
            colorCount: 8, paletteKind: PaletteKind.Rugged);
        var restored = MetadataPayload.Deserialize(rugged.Serialize());
        Assert.Equal(PaletteKind.Rugged, restored.PaletteKind);
        Assert.Equal(8, restored.ColorCount);
    }

    [Fact]
    public void Constructor_RejectsRuggedWithNonEightColourCount()
    {
        Assert.Throws<ArgumentException>(() => new MetadataPayload(
            Filled(0), PayloadType.Raw, EccLevel.Low, 1, 0, "x", 0, Filled(0),
            colorCount: 256, paletteKind: PaletteKind.Rugged));
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
    public void SerializeDeserialize_HandlesMaxLengthName()
    {
        var name = new string('x', MetadataPayload.MaxNameBytes);
        var original = CreateValid(name: name);

        var restored = MetadataPayload.Deserialize(original.Serialize());

        Assert.Equal(name, restored.OriginalName);
    }

    [Fact]
    public void Serialize_ThrowsOnOverlongName_AndFitNameShortensIt()
    {
        var overlong = new string('x', MetadataPayload.MaxNameBytes + 1);

        Assert.Throws<InvalidOperationException>(() => CreateValid(name: overlong).Serialize());

        var fitted = MetadataPayload.FitName(overlong);
        Assert.Equal(MetadataPayload.MaxNameBytes, fitted.Length);
        _ = CreateValid(name: fitted).Serialize();
    }

    [Fact]
    public void FitName_NeverSplitsAMultiByteCodePoint()
    {
        var fitted = MetadataPayload.FitName(string.Concat(Enumerable.Repeat("🎨", 200)));

        Assert.True(System.Text.Encoding.UTF8.GetByteCount(fitted) <= MetadataPayload.MaxNameBytes);
        Assert.Equal(0, fitted.Length % 2);
        Assert.Equal(fitted, MetadataPayload.FitName(fitted));
    }

    [Fact]
    public void Serialize_SizeIsFixedSizePlusNameBytes()
    {
        var payload = CreateValid(name: "abc");

        Assert.Equal(MetadataPayload.FixedSize + 3, payload.Serialize().Length);
    }

    [Fact]
    public void Serialize_FitsFrameZero_WithAMaxLengthName()
    {
        var payload = CreateValid(name: new string('n', MetadataPayload.MaxNameBytes));

        Assert.True(payload.Serialize().Length <= BootstrapFrame.ContentBytes,
            "Frame 0 must always fit the bootstrap frame's capacity.");
    }

    [Fact]
    public void SerializeDeserialize_PreservesMetadataFrameCount()
    {
        var original = new MetadataPayload(
            Filled(0xAA), PayloadType.Raw, EccLevel.Medium, 5, 1000, "x", 2000, Filled(0xBB),
            metadataFrameCount: 2);

        Assert.Equal(2, MetadataPayload.Deserialize(original.Serialize()).MetadataFrameCount);
        Assert.Equal(1, MetadataPayload.Deserialize(CreateValid().Serialize()).MetadataFrameCount);
    }

    [Fact]
    public void Constructor_RejectsMetadataFrameCountOutsideTotalFrames()
    {
        Assert.Throws<ArgumentException>(() => new MetadataPayload(
            Filled(0), PayloadType.Raw, EccLevel.Low, 1, 0, "x", 0, Filled(0), metadataFrameCount: 0));
        Assert.Throws<ArgumentException>(() => new MetadataPayload(
            Filled(0), PayloadType.Raw, EccLevel.Low, 2, 0, "x", 0, Filled(0), metadataFrameCount: 3));
    }

    [Fact]
    public void Deserialize_AcceptsNewerVersion_AndIgnoresAppendedFields()
    {
        var data = CreateValid().Serialize();
        var extended = data.Concat(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }).ToArray();
        extended[0] = MetadataPayload.CurrentVersion + 1;

        var restored = MetadataPayload.Deserialize(extended);

        Assert.Equal(MetadataPayload.CurrentVersion + 1, restored.Version);
        Assert.Equal("document.pdf", restored.OriginalName);
        Assert.True(restored.TryBuildLayout(out _));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
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
    public void TryBuildLayout_AcceptsGridPastOldHeaderLimit()
    {
        // 400×200 @ Low carries >65,535 B/frame — rejected under the old ushort field, allowed now
        // that FrameHeader.PayloadLength is a uint.
        var payload = new MetadataPayload(
            Filled(0xAA), PayloadType.SevenZip, EccLevel.Low, 42, 400_000, "d.pdf", 1_200_000, Filled(0xBB), 256)
        {
            GridWidthTiles = 400,
            GridHeightTiles = 200,
        };

        Assert.True(payload.TryBuildLayout(out var layout));
        int bytesPerFrame = payload.EccLevel.PayloadBytesPerFrame(layout!.CodewordsForBits(payload.BitsPerTile));
        Assert.True(bytesPerFrame > ushort.MaxValue);
    }
}
