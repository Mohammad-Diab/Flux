using System.Buffers.Binary;

namespace FluxCore.Framing;

/// <summary>
/// Represents the header of a single frame in the encoding.
/// Fixed size: 27 bytes (EncodingCode + FrameId + TotalFrames + Width + Height + CRC32 + Reserved).
/// </summary>
public readonly struct FrameHeader : IEquatable<FrameHeader>
{
    /// <summary>
    /// Size of the serialized header in bytes.
    /// </summary>
    public const int Size = 27;

  /// <summary>
    /// Gets the encoding code/version (1 byte). Current version = 1.
    /// </summary>
    public byte EncodingCode { get; init; }

/// <summary>
    /// Gets the frame ID (0-based index). Frame 0 is the metadata frame.
    /// </summary>
    public uint FrameId { get; init; }

    /// <summary>
    /// Gets the total number of frames in the encoding.
    /// </summary>
    public uint TotalFrames { get; init; }

    /// <summary>
    /// Gets the frame width in tiles.
    /// </summary>
    public ushort FrameWidthTiles { get; init; }

    /// <summary>
    /// Gets the frame height in tiles.
    /// </summary>
    public ushort FrameHeightTiles { get; init; }

    /// <summary>
    /// Gets the CRC-32 checksum of the frame payload (excluding this header).
    /// </summary>
    public uint Crc32 { get; init; }

    /// <summary>
    /// Initializes a new instance of the <see cref="FrameHeader"/> struct.
  /// </summary>
    public FrameHeader(byte encodingCode, uint frameId, uint totalFrames, ushort widthTiles, ushort heightTiles, uint crc32)
    {
        EncodingCode = encodingCode;
        FrameId = frameId;
TotalFrames = totalFrames;
      FrameWidthTiles = widthTiles;
        FrameHeightTiles = heightTiles;
        Crc32 = crc32;
    }

    /// <summary>
   /// Serializes the header to a byte array (27 bytes, little-endian).
    /// </summary>
  public byte[] Serialize()
    {
        var buffer = new byte[Size];
        Serialize(buffer);
        return buffer;
    }

    /// <summary>
    /// Serializes the header into the provided span (must be at least 27 bytes).
    /// </summary>
    public void Serialize(Span<byte> buffer)
 {
  if (buffer.Length < Size)
          throw new ArgumentException($"Buffer must be at least {Size} bytes.", nameof(buffer));

  int offset = 0;
     buffer[offset++] = EncodingCode;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[offset..], FrameId);
        offset += 4;
 BinaryPrimitives.WriteUInt32LittleEndian(buffer[offset..], TotalFrames);
        offset += 4;
BinaryPrimitives.WriteUInt16LittleEndian(buffer[offset..], FrameWidthTiles);
     offset += 2;
        BinaryPrimitives.WriteUInt16LittleEndian(buffer[offset..], FrameHeightTiles);
        offset += 2;
     BinaryPrimitives.WriteUInt32LittleEndian(buffer[offset..], Crc32);
        offset += 4;

   // Reserved: 8 bytes (zeros)
        buffer.Slice(offset, 8).Clear();
    }

    /// <summary>
    /// Deserializes a frame header from a byte array.
    /// </summary>
    public static FrameHeader Deserialize(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return Deserialize(data.AsSpan());
    }

    /// <summary>
  /// Deserializes a frame header from a span.
    /// </summary>
    public static FrameHeader Deserialize(ReadOnlySpan<byte> data)
    {
     if (data.Length < Size)
      throw new ArgumentException($"Data must be at least {Size} bytes.", nameof(data));

        int offset = 0;
        var encodingCode = data[offset++];
var frameId = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
        offset += 4;
        var totalFrames = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
        offset += 4;
     var widthTiles = BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
    offset += 2;
    var heightTiles = BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
  offset += 2;
        var crc32 = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);

        // Skip reserved 8 bytes

        return new FrameHeader(encodingCode, frameId, totalFrames, widthTiles, heightTiles, crc32);
    }

    public bool Equals(FrameHeader other) =>
      EncodingCode == other.EncodingCode &&
  FrameId == other.FrameId &&
        TotalFrames == other.TotalFrames &&
        FrameWidthTiles == other.FrameWidthTiles &&
     FrameHeightTiles == other.FrameHeightTiles &&
Crc32 == other.Crc32;

    public override bool Equals(object? obj) => obj is FrameHeader other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(EncodingCode, FrameId, TotalFrames, FrameWidthTiles, FrameHeightTiles, Crc32);

    public static bool operator ==(FrameHeader left, FrameHeader right) => left.Equals(right);

    public static bool operator !=(FrameHeader left, FrameHeader right) => !left.Equals(right);
}
