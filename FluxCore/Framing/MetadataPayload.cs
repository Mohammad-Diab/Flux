using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
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
    /// <summary>Metadata format version (current = 5).</summary>
    public const byte CurrentVersion = 5;

    /// <summary>Serialized size in bytes excluding the variable-length name.</summary>
    public const int FixedSize = 1 + 1 + 32 + 1 + 1 + 1 + 2 + 2 + 4 + 8 + 8 + 32 + 2 + 1 + 2;

    /// <summary>Largest UTF-8 name that fits frame 0 alongside the fixed fields.</summary>
    public const int MaxNameBytes = BootstrapFrame.ContentBytes - FixedSize;

    /// <summary>Gets the metadata format version.</summary>
    public byte Version { get; init; } = CurrentVersion;

    /// <summary>Gets how many leading frames carry metadata (frame 0 included); payload frames start at this id.</summary>
    public byte MetadataFrameCount { get; }

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

    /// <summary>Gets the data-tile palette family; regenerated with <see cref="ColorCount"/> via <see cref="PaletteGenerator"/>.</summary>
    public PaletteKind PaletteKind { get; }

    /// <summary>Gets the colour depth in bits per tile (the base-2 log of <see cref="ColorCount"/>).</summary>
    public int BitsPerTile => PaletteGenerator.BitsForCount(ColorCount);

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
        int colorCount = 256,
        PaletteKind paletteKind = PaletteKind.Standard,
        byte metadataFrameCount = 1)
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
        if (metadataFrameCount < 1 || metadataFrameCount > totalFrames)
            throw new ArgumentException(
                $"Metadata frame count {metadataFrameCount} must be between 1 and the {totalFrames} total frames.",
                nameof(metadataFrameCount));
        if (payloadLength < 0)
            throw new ArgumentException("Payload length cannot be negative.", nameof(payloadLength));
        if (originalLength < 0)
            throw new ArgumentException("Original length cannot be negative.", nameof(originalLength));
        if (!PaletteGenerator.IsSupportedCount(colorCount, paletteKind))
            throw new ArgumentException($"Unsupported colour count {colorCount} for {paletteKind} palette.", nameof(colorCount));

        Sha256 = sha256;
        PayloadType = payloadType;
        EccLevel = eccLevel;
        TotalFrames = totalFrames;
        PayloadLength = payloadLength;
        OriginalName = originalName;
        OriginalLength = originalLength;
        ContentSignature = contentSignature;
        ColorCount = colorCount;
        PaletteKind = paletteKind;
        MetadataFrameCount = metadataFrameCount;
    }

    /// <summary>
    /// Builds the payload-frame layout this transfer's geometry describes. The decoder adopts the
    /// returned layout for every payload frame (frame 0 is always <see cref="BootstrapFrame.Layout"/>).
    /// Returns false when the version is incompatible, the grid is not a constructible layout, or a
    /// frame's payload would overflow <see cref="FrameHeader.PayloadLength"/>; a decoder must refuse
    /// the transfer in that case.
    /// </summary>
    /// <param name="layout">The adopted payload layout when this returns true.</param>
    public bool TryBuildLayout([NotNullWhen(true)] out FrameLayout? layout)
    {
        layout = null;
        if (Version < CurrentVersion)
            return false;

        try
        {
            var candidate = new FrameLayout(GridWidthTiles, GridHeightTiles, TilePixelSize, BitsPerTile);
            if ((long)EccLevel.PayloadBytesPerFrame(candidate.CodewordsForBits(BitsPerTile)) > uint.MaxValue)
                return false;

            layout = candidate;
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Shortens a name to <see cref="MaxNameBytes"/> of UTF-8, never splitting a code point.</summary>
    /// <param name="name">Original file or folder name.</param>
    public static string FitName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (Encoding.UTF8.GetByteCount(name) <= MaxNameBytes)
            return name;

        int length = Math.Min(name.Length, MaxNameBytes);
        while (length > 0 &&
               (char.IsHighSurrogate(name[length - 1]) || Encoding.UTF8.GetByteCount(name, 0, length) > MaxNameBytes))
            length--;
        return name[..length];
    }

    /// <summary>
    /// Serializes the metadata payload. Layout (little-endian, version 5):
    /// Version(1) | MetadataFrameCount(1) | Sha256(32) | PayloadType(1) | EccLevel(1) |
    /// TilePixelSize(1) | GridWidthTiles(2) | GridHeightTiles(2) | TotalFrames(4) |
    /// PayloadLength(8) | OriginalLength(8) | ContentSignature(32) | ColorCount(2) |
    /// PaletteKind(1) | NameLength(2) | Name(UTF-8). The name comes last so every fixed field has
    /// a constant offset; from v5 on, new versions may only append fields, which readers ignore.
    /// </summary>
    public byte[] Serialize()
    {
        var nameBytes = Encoding.UTF8.GetBytes(OriginalName);
        if (nameBytes.Length > MaxNameBytes)
            throw new InvalidOperationException(
                $"Original name is too long: {nameBytes.Length} bytes (max {MaxNameBytes}; shorten it with {nameof(FitName)}).");

        var buffer = new byte[FixedSize + nameBytes.Length];
        int offset = 0;

        buffer[offset++] = Version;
        buffer[offset++] = MetadataFrameCount;

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
        BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(offset), OriginalLength);
        offset += 8;

        ContentSignature.CopyTo(buffer.AsSpan(offset));
        offset += 32;

        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(offset), (ushort)ColorCount);
        offset += 2;

        buffer[offset++] = (byte)PaletteKind;

        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(offset), (ushort)nameBytes.Length);
        offset += 2;
        nameBytes.CopyTo(buffer.AsSpan(offset));

        return buffer;
    }

    /// <summary>Deserializes a metadata payload from a byte array.</summary>
    /// <param name="data">Serialized metadata.</param>
    public static MetadataPayload Deserialize(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return Deserialize(data.AsSpan());
    }

    /// <summary>
    /// Deserializes a metadata payload from a span. Accepts any version at or above
    /// <see cref="CurrentVersion"/>: from v5 on, new versions only ever append fields, so the
    /// known prefix parses and the remainder is ignored.
    /// </summary>
    /// <param name="data">Serialized metadata.</param>
    public static MetadataPayload Deserialize(ReadOnlySpan<byte> data)
    {
        if (data.Length < FixedSize)
            throw new ArgumentException("Data is too short to be a valid metadata payload.", nameof(data));

        int offset = 0;

        var version = data[offset++];
        if (version < CurrentVersion)
            throw new NotSupportedException(
                $"Unsupported metadata version: {version}. Expected {CurrentVersion} or later.");

        var metadataFrameCount = data[offset++];

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
        var originalLength = BinaryPrimitives.ReadInt64LittleEndian(data[offset..]);
        offset += 8;

        var contentSignature = data.Slice(offset, 32).ToArray();
        offset += 32;

        var colorCount = BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
        offset += 2;

        var paletteKind = (PaletteKind)data[offset++];

        var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
        offset += 2;

        if (offset + nameLength > data.Length)
            throw new ArgumentException("Data is corrupted or truncated.", nameof(data));

        var originalName = Encoding.UTF8.GetString(data.Slice(offset, nameLength));

        return new MetadataPayload(
            sha256,
            payloadType,
            eccLevel,
            totalFrames,
            payloadLength,
            originalName,
            originalLength,
            contentSignature,
            colorCount,
            paletteKind,
            metadataFrameCount)
        {
            Version = version,
            TilePixelSize = tilePixelSize,
            GridWidthTiles = gridWidthTiles,
            GridHeightTiles = gridHeightTiles,
        };
    }
}
