using FluxCore.Ecc;
using FluxCore.Hashing;
using FluxCore.Imaging;

namespace FluxCore.Framing;

/// <summary>
/// High-level service for decoding frames back to original payload.
/// </summary>
public sealed class FrameDecodingService
{
    private MetadataPayload? _metadata;
    private FrameLayout? _layout;
    private ReedSolomonEcc? _ecc;

    /// <summary>
    /// Gets the decoded metadata (available after DecodeMetadataFrame).
    /// </summary>
    public MetadataPayload? Metadata => _metadata;

    /// <summary>
    /// Decodes the metadata frame (Frame 0) and initializes the decoder.
/// </summary>
    /// <param name="metadataFrameImage">PNG image data of Frame 0.</param>
    public void DecodeMetadataFrame(byte[] metadataFrameImage)
    {
   ArgumentNullException.ThrowIfNull(metadataFrameImage);

        using var decoder = new FrameDecoder(ColorMap.Default); // Use default to bootstrap
  _metadata = decoder.DecodeMetadataFrame(metadataFrameImage);

        // Initialize layout and ECC based on metadata
  _layout = new FrameLayout(
            frameWidthPx: 800, // Would come from external source or be calculated
     frameHeightPx: 600,
     (TileSize)_metadata.TileSize,
   _metadata.SeparatorEvery);

  if (_metadata.Algorithm == 1 && _metadata.EccPer16 > 0)
  {
   _ecc = new ReedSolomonEcc(_metadata.EccPer16);
        }
    }

    /// <summary>
    /// Decodes data frames and assembles the complete payload.
    /// </summary>
    /// <param name="frameImages">List of data frame images (excluding Frame 0).</param>
    /// <returns>Reconstructed payload.</returns>
    /// <exception cref="InvalidOperationException">Thrown when metadata hasn't been decoded first.</exception>
    public byte[] DecodeFrames(List<byte[]> frameImages)
    {
 if (_metadata == null || _layout == null)
  throw new InvalidOperationException("Metadata frame must be decoded first.");

     ArgumentNullException.ThrowIfNull(frameImages);

        using var decoder = new FrameDecoder(_metadata.ColorMap, _ecc);

        var payloadSegments = new List<byte[]>();
     int expectedOriginalLength = CalculateFrameCapacity();

  foreach (var frameImage in frameImages)
   {
   var result = decoder.DecodeFrame(frameImage, expectedOriginalLength);

       // Verify CRC
if (!result.CrcValid)
       {
 // Log warning but continue - ECC may have corrected errors
   }

       payloadSegments.Add(result.Payload);
     }

 // Concatenate all segments
 int totalLength = payloadSegments.Sum(s => s.Length);
   var fullPayload = new byte[totalLength];
        int offset = 0;

  foreach (var segment in payloadSegments)
   {
     segment.CopyTo(fullPayload, offset);
        offset += segment.Length;
  }

   // Trim to original length if needed
        if (fullPayload.Length > _metadata.OriginalLength)
 {
   var trimmed = new byte[_metadata.OriginalLength];
   Array.Copy(fullPayload, trimmed, trimmed.Length);
    fullPayload = trimmed;
 }

        // Verify SHA-256
     var computedHash = Sha256Helper.ComputeHash(fullPayload);
        if (!Sha256Helper.Verify(fullPayload, _metadata.Sha256))
        {
            throw new InvalidOperationException("SHA-256 verification failed. Payload is corrupted.");
   }

    return fullPayload;
    }

    private int CalculateFrameCapacity()
    {
 if (_layout == null)
            return 0;

  int capacity = _layout.TotalTiles;
   if (_ecc != null)
   {
 int dataBlocks = (capacity + 15) / 16;
int eccSymbols = dataBlocks * _ecc.EccSymbolsPer16;
     capacity -= eccSymbols;
  }

   return capacity;
  }
}
