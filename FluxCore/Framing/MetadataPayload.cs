using System.Buffers.Binary;
using System.Text;
using FluxCore.Ecc;
using FluxCore.Imaging;

namespace FluxCore.Framing;

/// <summary>
/// Payload of frame 0: transfer parameters and integrity information. Lets a decoder fail fast
/// on version or geometry mismatch and verify the reassembled payload end to end. Always encoded
/// at <see cref="EccLevel.Max"/> regardless of the transfer's payload level.
/// </summary>
public sealed class MetadataPayload
{
    /// <summary>Metadata format version (current = 3).</summary>
    public const byte CurrentVersion = 3;

    /// <summary>Serialized size in bytes excluding the variable-length name.</summary>
    public const int FixedSize = 1 + 32 + 1 + 1 + 1 + 2 + 2 + 4 + 8 + 2 + 8 + 32 + 2;

    /// <summary>Gets the metadata format version.</summary>
    public byte Version { get; init; } = CurrentVersion;

    /// <summary>Gets the SHA-256 hash of the transferred payload (after compression if applicable).</summary>
    public byte[] Sha256 { get; }

    /// <summary>Gets the payload type (raw or 7z).</summary>
    public PayloadType PayloadType { get; }

    /// <summary>Gets the ECC level used for payload frames (frame 0 itself always uses Max).</summary>
    public EccLevel EccLevel { get; }

    /// <summary>Gets the tile edge length in pixels, echoed so a decoder can verify geometry.</summary>
    public byte TilePixelSize { get; init; } = FrameFormat.TilePixelSize;

    /// <summary>Gets the grid width in tiles, echoed so a decoder can verify geometry.</summary>
    public ushort GridWidthTiles { get; init; } = FrameFormat.GridWidthTiles;

    /// <summary>Gets the grid height in tiles, echoed so a decoder can verify geometry.</summary>
    public ushort GridHeightTiles { get; init; } = FrameFormat.GridHeightTiles;

    /// <summary>Gets the total number of frames in the transfer, including frame 0.</summary>
    public uint TotalFrames { get; }

    /// <summary>Gets the total transferred payload length in bytes (compressed size for 7z payloads).</summary>
    public long PayloadLength { get; }

    /// <summary>Gets the original file or folder name.</summary>
    public string OriginalName { get; }

    /// <summary>Gets the original uncompressed length in bytes.</summary>
    public long OriginalLength { get; }

    /// <summary>Gets the 32-byte content signature identifying the source (used for session/resume naming).</summary>
    public byte[] ContentSignature { get; }

    /// <summary>Gets the data-tile colour count; the palette is regenerated from it via <see cref="PaletteGenerator"/>.</summary>
    public int ColorCount { get; }

    /// <summary>Creates and validates the transfer metadata.</summary>
    public MetadataPayload(
        byte[] sha256,
        PayloadType payloadType,
        EccLevel eccLevel,
        uint totalFrames,
        long payloadLength,
        string originalName,
        long originalLength,
        byte[] contentSignature,
        int colorCount = 256)
    {
        ArgumentNullException.ThrowIfNull(sha256);
        ArgumentNullException.ThrowIfNull(originalName);
        ArgumentNullException.ThrowIfNull(contentSignature);

        if (sha256.Length != 32)
            throw new ArgumentException("SHA-256 must be 32 bytes.", nameof(sha256));
        if (contentSignature.Length != 32)
            throw new ArgumentException("Content signature must be 32 bytes.", nameof(contentSignature));
        if ((byte)eccLevel > (byte)EccLevel.Max)
            throw new ArgumentException($"Unknown ECC level: {eccLevel}.", nameof(eccLevel));
        if (totalFrames < 1)
            throw new ArgumentException("Total frames must be at least 1.", nameof(totalFrames));
        if (payloadLength < 0)
            throw new ArgumentException("Payload length cannot be negative.", nameof(payloadLength));
        if (originalLength < 0)
            throw new ArgumentException("Original length cannot be negative.", nameof(originalLength));
        if (!PaletteGenerator.IsSupportedCount(colorCount))
            throw new ArgumentException($"Unsupported colour count: {colorCount}.", nameof(colorCount));

        Sha256 = sha256;
        PayloadType = payloadType;
        EccLevel = eccLevel;
        TotalFrames = totalFrames;
        PayloadLength = payloadLength;
        OriginalName = originalName;
        OriginalLength = originalLength;
        ContentSignature = contentSignature;
        ColorCount = colorCount;
    }

    /// <summary>
    /// Determines whether the echoed geometry matches this library's fixed frame format.
    /// A decoder must refuse the transfer when this returns false.
    /// </summary>
    public bool MatchesFrameFormat() =>
        Version == CurrentVersion &&
        TilePixelSize == FrameFormat.TilePixelSize &&
        GridWidthTiles == FrameFormat.GridWidthTiles &&
        GridHeightTiles == FrameFormat.GridHeightTiles;

    /// <summary>
    /// Serializes the metadata payload. Layout (little-endian):
    /// Version(1) | Sha256(32) | PayloadType(1) | EccLevel(1) | TilePixelSize(1) |
    /// GridWidthTiles(2) | GridHeightTiles(2) | TotalFrames(4) | PayloadLength(8) |
    /// NameLength(2) | Name(UTF-8) | OriginalLength(8) | ContentSignature(32) | ColorCount(2).
    /// </summary>
    public byte[] Serialize()
    {
        var nameBytes = Encoding.UTF8.GetBytes(OriginalName);
        if (nameBytes.Length > ushort.MaxValue)
            throw new InvalidOperationException(
                $"Original name is too long: {nameBytes.Length} bytes (max {ushort.MaxValue}).");

        var buffer = new byte[FixedSize + nameBytes.Length];
        int offset = 0;

        buffer[offset++] = Version;

        Sha256.CopyTo(buffer.AsSpan(offset));
        offset += 32;

        buffer[offset++] = (byte)PayloadType;
        buffer[offset++] = (byte)EccLevel;
        buffer[offset++] = TilePixelSize;

        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(offset), GridWidthTiles);
        offset += 2;
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(offset), GridHeightTiles);
        offset += 2;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset), TotalFrames);
        offset += 4;
        BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(offset), PayloadLength);
        offset += 8;

        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(offset), (ushort)nameBytes.Length);
        offset += 2;
        nameBytes.CopyTo(buffer.AsSpan(offset));
        offset += nameBytes.Length;

        BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(offset), OriginalLength);
        offset += 8;

        ContentSignature.CopyTo(buffer.AsSpan(offset));
        offset += 32;

        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(offset), (ushort)ColorCount);

        return buffer;
    }

    /// <summary>Deserializes a metadata payload from a byte array.</summary>
    /// <param name="data">Serialized metadata.</param>
    public static MetadataPayload Deserialize(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return Deserialize(data.AsSpan());
    }

    /// <summary>Deserializes a metadata payload from a span.</summary>
    /// <param name="data">Serialized metadata.</param>
    public static MetadataPayload Deserialize(ReadOnlySpan<byte> data)
    {
        if (data.Length < FixedSize)
            throw new ArgumentException("Data is too short to be a valid metadata payload.", nameof(data));

        int offset = 0;

        var version = data[offset++];
        if (version != CurrentVersion)
            throw new NotSupportedException(
                $"Unsupported metadata version: {version}. Expected {CurrentVersion}.");

        var sha256 = data.Slice(offset, 32).ToArray();
        offset += 32;

        var payloadType = (PayloadType)data[offset++];
        var eccLevel = (EccLevel)data[offset++];
        var tilePixelSize = data[offset++];

        var gridWidthTiles = BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
        offset += 2;
        var gridHeightTiles = BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
        offset += 2;
        var totalFrames = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
        offset += 4;
        var payloadLength = BinaryPrimitives.ReadInt64LittleEndian(data[offset..]);
        offset += 8;

        var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
        offset += 2;

        if (offset + nameLength + 8 + 32 + 2 > data.Length)
            throw new ArgumentException("Data is corrupted or truncated.", nameof(data));

        var originalName = Encoding.UTF8.GetString(data.Slice(offset, nameLength));
        offset += nameLength;

        var originalLength = BinaryPrimitives.ReadInt64LittleEndian(data[offset..]);
        offset += 8;

        var contentSignature = data.Slice(offset, 32).ToArray();
        offset += 32;

        var colorCount = BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);

        return new MetadataPayload(
            sha256,
            payloadType,
            eccLevel,
            totalFrames,
            payloadLength,
            originalName,
            originalLength,
            contentSignature,
            colorCount)
        {
            Version = version,
            TilePixelSize = tilePixelSize,
            GridWidthTiles = gridWidthTiles,
            GridHeightTiles = gridHeightTiles,
        };
    }
}
