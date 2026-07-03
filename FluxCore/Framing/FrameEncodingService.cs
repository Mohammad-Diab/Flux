using FluxCore.Ecc;
using FluxCore.Hashing;
using FluxCore.Imaging;

namespace FluxCore.Framing;

/// <summary>
/// High-level service for encoding complete payloads into multiple frames.
/// </summary>
public sealed class FrameEncodingService
{
    private readonly FrameLayout _layout;
    private readonly ColorMap _colorMap;
  private readonly ReedSolomonEcc? _ecc;
    private readonly byte _encodingCode;

    /// <summary>
    /// Initializes a new instance of the <see cref="FrameEncodingService"/> class.
    /// </summary>
    public FrameEncodingService(FrameLayout layout, ColorMap colorMap, ReedSolomonEcc? ecc = null, byte encodingCode = 1)
    {
        ArgumentNullException.ThrowIfNull(layout);
  ArgumentNullException.ThrowIfNull(colorMap);

  _layout = layout;
   _colorMap = colorMap;
        _ecc = ecc;
    _encodingCode = encodingCode;
    }

    /// <summary>
    /// Segments a payload into frames and encodes them.
    /// </summary>
    /// <param name="payload">Complete payload to encode.</param>
    /// <param name="metadata">Metadata for Frame 0.</param>
    /// <returns>List of encoded frame images (Frame 0 is first).</returns>
    public List<byte[]> EncodePayload(byte[] payload, MetadataPayload metadata)
 {
 ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(metadata);

        var frames = new List<byte[]>();

        // Calculate frame capacity (accounting for ECC overhead)
   int frameCapacity = _layout.TotalTiles;
        if (_ecc != null)
   {
            // Subtract ECC overhead
   int dataBlocks = (frameCapacity + 15) / 16;
       int eccSymbols = dataBlocks * _ecc.EccSymbolsPer16;
     frameCapacity -= eccSymbols;
   }

     // Calculate total frames needed
        int totalDataFrames = (payload.Length + frameCapacity - 1) / frameCapacity;
   uint totalFrames = (uint)(totalDataFrames + 1); // +1 for metadata frame

   using var encoder = new FrameEncoder(_layout, _colorMap, _ecc, _encodingCode);

// Encode metadata frame (Frame 0)
        var metadataFrame = encoder.EncodeMetadataFrame(metadata, totalFrames);
        frames.Add(metadataFrame);

// Encode data frames
   int offset = 0;
   uint frameId = 1;

   while (offset < payload.Length)
     {
   int chunkSize = Math.Min(frameCapacity, payload.Length - offset);
    var chunk = new byte[chunkSize];
            Array.Copy(payload, offset, chunk, 0, chunkSize);

   var frameImage = encoder.EncodeFrame(chunk, frameId, totalFrames);
    frames.Add(frameImage);

      offset += chunkSize;
   frameId++;
   }

   return frames;
    }

    /// <summary>
    /// Computes SHA-256 hash of payload for metadata.
 /// </summary>
    public static byte[] ComputePayloadHash(byte[] payload)
    {
  return Sha256Helper.ComputeHash(payload);
    }
}
