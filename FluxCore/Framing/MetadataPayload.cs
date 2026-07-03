using FluxCore.Imaging;
using System.Buffers.Binary;
using System.Text;

namespace FluxCore.Framing;

/// <summary>
/// Metadata payload for Frame 0, containing encoding parameters and integrity information.
/// </summary>
public sealed class MetadataPayload
{
    /// <summary>
    /// Metadata version (current = 1).
    /// </summary>
    public const byte CurrentVersion = 1;

    /// <summary>
    /// Gets the metadata version.
    /// </summary>
    public byte Version { get; init; } = CurrentVersion;

    /// <summary>
    /// Gets the SHA-256 hash of the full payload (after compression if applicable).
    /// </summary>
    public byte[] Sha256 { get; init; }

    /// <summary>
    /// Gets the tile size in pixels.
    /// </summary>
    public byte TileSize { get; init; }

    /// <summary>
    /// Gets the number of ECC symbols per 16 data symbols (1-8).
    /// </summary>
    public byte EccPer16 { get; init; }

    /// <summary>
    /// Gets the separator interval in tiles (typically 8).
    /// </summary>
    public byte SeparatorEvery { get; init; }

    /// <summary>
    /// Gets the ECC algorithm identifier (1 = Reed-Solomon, 0 = none).
    /// </summary>
    public byte Algorithm { get; init; }

    /// <summary>
    /// Gets the specification version.
    /// </summary>
    public byte SpecVersion { get; init; } = 1;

    /// <summary>
    /// Gets the payload type (raw or 7z).
    /// </summary>
    public PayloadType PayloadType { get; init; }

    /// <summary>
  /// Gets the original file/folder name.
    /// </summary>
    public string OriginalName { get; init; }

    /// <summary>
    /// Gets the original uncompressed length in bytes.
    /// </summary>
    public long OriginalLength { get; init; }

    /// <summary>
    /// Gets the color map used for encoding.
    /// </summary>
    public ColorMap ColorMap { get; init; }

    /// <summary>
    /// Initializes a new instance of the <see cref="MetadataPayload"/> class.
    /// </summary>
    public MetadataPayload(
        byte[] sha256,
        byte tileSize,
    byte eccPer16,
   byte separatorEvery,
      byte algorithm,
   PayloadType payloadType,
        string originalName,
  long originalLength,
        ColorMap colorMap)
    {
    ArgumentNullException.ThrowIfNull(sha256);
        ArgumentNullException.ThrowIfNull(originalName);
        ArgumentNullException.ThrowIfNull(colorMap);

        if (sha256.Length != 32)
            throw new ArgumentException("SHA-256 must be 32 bytes.", nameof(sha256));

        if (eccPer16 < 0 || eccPer16 > 8)
     throw new ArgumentException("ECC per 16 must be between 0 and 8.", nameof(eccPer16));

     Sha256 = sha256;
        TileSize = tileSize;
        EccPer16 = eccPer16;
        SeparatorEvery = separatorEvery;
    Algorithm = algorithm;
        PayloadType = payloadType;
   OriginalName = originalName;
        OriginalLength = originalLength;
        ColorMap = colorMap;
    }

    /// <summary>
    /// Serializes the metadata payload to a byte array.
    /// Format:
    ///   - Version: 1 byte
    ///   - SHA256: 32 bytes
    ///   - TileSize: 1 byte
    ///   - EccPer16: 1 byte
    ///   - SeparatorEvery: 1 byte
    ///   - Algorithm: 1 byte
    ///   - SpecVersion: 1 byte
    ///   - PayloadType: 1 byte
    ///   - OriginalNameLength: 2 bytes (ushort)
    ///   - OriginalName: variable UTF-8
    ///   - OriginalLength: 8 bytes (long)
    ///   - ColorMap: 768 bytes (256 colors × 3 RGB)
    /// </summary>
    public byte[] Serialize()
    {
      var nameBytes = Encoding.UTF8.GetBytes(OriginalName);
    if (nameBytes.Length > ushort.MaxValue)
  throw new InvalidOperationException($"Original name is too long: {nameBytes.Length} bytes (max {ushort.MaxValue}).");

        var colorMapBytes = ColorMap.Serialize();

        // Calculate total size
        int totalSize = 1 + 32 + 1 + 1 + 1 + 1 + 1 + 1 + 2 + nameBytes.Length + 8 + 768;
        var buffer = new byte[totalSize];
  int offset = 0;

  // Write fields
        buffer[offset++] = Version;
        
      Sha256.CopyTo(buffer.AsSpan(offset));
        offset += 32;

        buffer[offset++] = TileSize;
        buffer[offset++] = EccPer16;
        buffer[offset++] = SeparatorEvery;
        buffer[offset++] = Algorithm;
   buffer[offset++] = SpecVersion;
        buffer[offset++] = (byte)PayloadType;

        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(offset), (ushort)nameBytes.Length);
        offset += 2;

  nameBytes.CopyTo(buffer.AsSpan(offset));
  offset += nameBytes.Length;

   BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(offset), OriginalLength);
        offset += 8;

   colorMapBytes.CopyTo(buffer.AsSpan(offset));

        return buffer;
    }

    /// <summary>
    /// Deserializes metadata payload from a byte array.
    /// </summary>
    public static MetadataPayload Deserialize(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return Deserialize(data.AsSpan());
    }

    /// <summary>
    /// Deserializes metadata payload from a span.
    /// </summary>
    public static MetadataPayload Deserialize(ReadOnlySpan<byte> data)
    {
        // Minimum size check (without name and color map)
        if (data.Length < 1 + 32 + 1 + 1 + 1 + 1 + 1 + 1 + 2 + 8 + 768)
       throw new ArgumentException("Data is too short to be a valid metadata payload.", nameof(data));

        int offset = 0;

     var version = data[offset++];
        if (version != CurrentVersion)
    throw new NotSupportedException($"Unsupported metadata version: {version}. Expected {CurrentVersion}.");

        var sha256 = data.Slice(offset, 32).ToArray();
        offset += 32;

        var tileSize = data[offset++];
 var eccPer16 = data[offset++];
        var separatorEvery = data[offset++];
        var algorithm = data[offset++];
        var specVersion = data[offset++];
        var payloadType = (PayloadType)data[offset++];

var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
offset += 2;

      if (offset + nameLength + 8 + 768 > data.Length)
            throw new ArgumentException("Data is corrupted or truncated.", nameof(data));

    var originalName = Encoding.UTF8.GetString(data.Slice(offset, nameLength));
        offset += nameLength;

      var originalLength = BinaryPrimitives.ReadInt64LittleEndian(data[offset..]);
        offset += 8;

  var colorMapBytes = data.Slice(offset, 768).ToArray();
var colorMap = ColorMap.Deserialize(colorMapBytes);

        return new MetadataPayload(
sha256,
            tileSize,
       eccPer16,
            separatorEvery,
            algorithm,
   payloadType,
            originalName,
      originalLength,
            colorMap)
        {
            Version = version,
 SpecVersion = specVersion
        };
    }
}
