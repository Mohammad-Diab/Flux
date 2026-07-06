using System.Buffers.Binary;
using FluxCore.Ecc;

namespace FluxCore.Framing;

/// <summary>
/// Fixed 16-byte per-frame header, embedded in the image as three redundant RS(48,16)-protected
/// copies. Self-describing: carries the ECC level of its own frame, so the decoder needs no
/// out-of-band configuration. Layout (little-endian):
/// FormatVersion(1) | Flags(1) | FrameId(4) | TotalFrames(4) | PayloadLength(2) | PayloadCrc32(4).
/// </summary>
public readonly struct FrameHeader : IEquatable<FrameHeader>
{
    /// <summary>Size of the serialized header in bytes.</summary>
    public const int Size = 16;

    private const byte MetadataFrameFlag = 0b0000_0001;
    private const byte EccLevelMask = 0b0000_0110;
    private const int EccLevelShift = 1;

    /// <summary>Gets the frame format version. Always <see cref="FrameFormat.Version"/> for frames this library produces.</summary>
    public byte FormatVersion { get; init; }

    /// <summary>Gets a value indicating whether this frame carries the metadata payload (frame 0).</summary>
    public bool IsMetadataFrame { get; init; }

    /// <summary>Gets the ECC level used for this frame's payload codewords.</summary>
    public EccLevel EccLevel { get; init; }

    /// <summary>Gets the frame ID (0-based; frame 0 is the metadata frame).</summary>
    public uint FrameId { get; init; }

    /// <summary>Gets the total number of frames in the transfer, including frame 0.</summary>
    public uint TotalFrames { get; init; }

    /// <summary>Gets the number of real payload bytes in this frame (the rest is zero padding).</summary>
    public ushort PayloadLength { get; init; }

    /// <summary>Gets the CRC-32 checksum over the first <see cref="PayloadLength"/> payload bytes.</summary>
    public uint PayloadCrc32 { get; init; }

    /// <summary>Creates a header stamped with the current format version.</summary>
    public FrameHeader(uint frameId, uint totalFrames, ushort payloadLength, uint payloadCrc32,
        EccLevel eccLevel, bool isMetadataFrame = false)
    {
        FormatVersion = FrameFormat.Version;
        FrameId = frameId;
        TotalFrames = totalFrames;
        PayloadLength = payloadLength;
        PayloadCrc32 = payloadCrc32;
        EccLevel = eccLevel;
        IsMetadataFrame = isMetadataFrame;
    }

    /// <summary>
    /// Determines whether the header's field values are internally consistent. Used to accept
    /// or reject a header recovered from a single copy when the other copies are damaged.
    /// </summary>
    public bool IsPlausible() =>
        FormatVersion == FrameFormat.Version &&
        (byte)EccLevel <= (byte)EccLevel.Max &&
        TotalFrames > 0 &&
        FrameId < TotalFrames &&
        PayloadLength <= EccLevel.PayloadBytesPerFrame() &&
        (!IsMetadataFrame || FrameId == 0);

    /// <summary>Serializes the header into a destination span of at least <see cref="Size"/> bytes.</summary>
    /// <param name="destination">Destination span.</param>
    public void Serialize(Span<byte> destination)
    {
        if (destination.Length < Size)
            throw new ArgumentException($"Destination must be at least {Size} bytes.", nameof(destination));

        destination[0] = FormatVersion;
        destination[1] = (byte)((IsMetadataFrame ? MetadataFrameFlag : 0) |
                                (((byte)EccLevel << EccLevelShift) & EccLevelMask));
        BinaryPrimitives.WriteUInt32LittleEndian(destination[2..], FrameId);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[6..], TotalFrames);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[10..], PayloadLength);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[12..], PayloadCrc32);
    }

    /// <summary>Serializes the header to a new 16-byte array.</summary>
    public byte[] Serialize()
    {
        var buffer = new byte[Size];
        Serialize(buffer);
        return buffer;
    }

    /// <summary>
    /// Deserializes a header from a span of at least <see cref="Size"/> bytes. Performs no
    /// plausibility validation; callers decide via <see cref="IsPlausible"/>.
    /// </summary>
    /// <param name="source">Source span.</param>
    public static FrameHeader Deserialize(ReadOnlySpan<byte> source)
    {
        if (source.Length < Size)
            throw new ArgumentException($"Source must be at least {Size} bytes.", nameof(source));

        return new FrameHeader
        {
            FormatVersion = source[0],
            IsMetadataFrame = (source[1] & MetadataFrameFlag) != 0,
            EccLevel = (EccLevel)((source[1] & EccLevelMask) >> EccLevelShift),
            FrameId = BinaryPrimitives.ReadUInt32LittleEndian(source[2..]),
            TotalFrames = BinaryPrimitives.ReadUInt32LittleEndian(source[6..]),
            PayloadLength = BinaryPrimitives.ReadUInt16LittleEndian(source[10..]),
            PayloadCrc32 = BinaryPrimitives.ReadUInt32LittleEndian(source[12..]),
        };
    }

    /// <inheritdoc/>
    public bool Equals(FrameHeader other) =>
        FormatVersion == other.FormatVersion &&
        IsMetadataFrame == other.IsMetadataFrame &&
        EccLevel == other.EccLevel &&
        FrameId == other.FrameId &&
        TotalFrames == other.TotalFrames &&
        PayloadLength == other.PayloadLength &&
        PayloadCrc32 == other.PayloadCrc32;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is FrameHeader other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        HashCode.Combine(FormatVersion, IsMetadataFrame, EccLevel, FrameId, TotalFrames, PayloadLength, PayloadCrc32);

    /// <summary>Equality operator.</summary>
    public static bool operator ==(FrameHeader left, FrameHeader right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(FrameHeader left, FrameHeader right) => !left.Equals(right);

    /// <inheritdoc/>
    public override string ToString() =>
        $"Frame {FrameId}/{TotalFrames} (v{FormatVersion}, {EccLevel}, {PayloadLength} bytes{(IsMetadataFrame ? ", metadata" : "")})";
}
