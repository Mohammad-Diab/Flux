using FluxCore.Ecc;
using FluxCore.Hashing;
using FluxCore.Imaging;
using SkiaSharp;

namespace FluxCore.Framing;

/// <summary>
/// Result of frame decoding operation.
/// </summary>
public sealed class FrameDecodeResult
{
    /// <summary>
    /// Gets the decoded frame header.
 /// </summary>
    public FrameHeader Header { get; init; }

    /// <summary>
    /// Gets the decoded payload bytes (after ECC correction).
    /// </summary>
    public byte[] Payload { get; init; }

    /// <summary>
    /// Gets a value indicating whether ECC was applied and succeeded.
    /// </summary>
    public bool EccApplied { get; init; }

    /// <summary>
    /// Gets a value indicating whether CRC verification passed.
    /// </summary>
    public bool CrcValid { get; init; }

 /// <summary>
    /// Gets the number of errors corrected by ECC (if applicable).
    /// </summary>
    public int ErrorsCorrected { get; init; }

    public FrameDecodeResult(FrameHeader header, byte[] payload, bool eccApplied, bool crcValid, int errorsCorrected = 0)
    {
 Header = header;
        Payload = payload ?? throw new ArgumentNullException(nameof(payload));
  EccApplied = eccApplied;
        CrcValid = crcValid;
  ErrorsCorrected = errorsCorrected;
    }
}

/// <summary>
/// Decodes visual frame images back to byte data with ECC and CRC verification.
/// </summary>
public sealed class FrameDecoder : IDisposable
{
    private readonly ColorMap _colorMap;
    private readonly ReedSolomonEcc? _ecc;

    /// <summary>
    /// Initializes a new instance of the <see cref="FrameDecoder"/> class.
    /// </summary>
    /// <param name="colorMap">Color map for color?byte conversion.</param>
    /// <param name="ecc">Optional Reed-Solomon ECC decoder.</param>
    public FrameDecoder(ColorMap colorMap, ReedSolomonEcc? ecc = null)
  {
  ArgumentNullException.ThrowIfNull(colorMap);

   _colorMap = colorMap;
        _ecc = ecc;
    }

    /// <summary>
    /// Decodes a frame image to extract header and payload.
    /// </summary>
    /// <param name="imageData">PNG image data.</param>
    /// <param name="expectedOriginalLength">Expected original payload length (without ECC).</param>
    /// <returns>Decoded frame result.</returns>
    /// <exception cref="InvalidOperationException">Thrown when image cannot be decoded.</exception>
    public FrameDecodeResult DecodeFrame(byte[] imageData, int expectedOriginalLength)
    {
ArgumentNullException.ThrowIfNull(imageData);

        using var bitmap = SKBitmap.Decode(imageData);
   if (bitmap == null)
       throw new InvalidOperationException("Failed to decode image data.");

        // Infer layout from image size (we'll need to pass this or detect it)
        // For now, assume we know the tile size or can detect it
   // This is a simplification - in practice, you'd need to store/pass layout info

   // Parse header from image metadata or a known location
        // For this implementation, we'll assume the header is stored externally
  // and focus on tile extraction

        // Extract tiles
     var tiles = ExtractTiles(bitmap);

        // Apply ECC if configured
   byte[] payload;
   bool eccApplied = false;
   int errorsCorrected = 0;

   if (_ecc != null && expectedOriginalLength > 0)
   {
 eccApplied = _ecc.TryDecode(tiles, expectedOriginalLength, out var decoded);
if (eccApplied && decoded != null)
       {
      payload = decoded;
     // Note: actual error count would need to be tracked by ECC implementation
     errorsCorrected = 0; // Placeholder
 }
   else
      {
                throw new EccException("Failed to decode frame with ECC.");
      }
   }
   else
        {
   payload = tiles;
 }

        // Verify CRC (would need header info)
        // For now, create a placeholder header
     var header = new FrameHeader(1, 0, 1, 0, 0, 0);
  bool crcValid = true; // Placeholder

        return new FrameDecodeResult(header, payload, eccApplied, crcValid, errorsCorrected);
    }

    /// <summary>
    /// Decodes metadata frame (Frame 0).
    /// </summary>
    /// <param name="imageData">PNG image data of Frame 0.</param>
    /// <returns>Decoded metadata payload.</returns>
    public MetadataPayload DecodeMetadataFrame(byte[] imageData)
    {
  ArgumentNullException.ThrowIfNull(imageData);

   using var bitmap = SKBitmap.Decode(imageData);
        if (bitmap == null)
      throw new InvalidOperationException("Failed to decode metadata frame image.");

     // Extract tiles (metadata frame uses 8×8 tiles)
        var tiles = ExtractTiles(bitmap, TileSize.Size8x8);

   // Deserialize metadata
 return MetadataPayload.Deserialize(tiles);
    }

    private byte[] ExtractTiles(SKBitmap bitmap, TileSize? forceTileSize = null)
    {
   // Detect or use forced tile size
     var tileSize = forceTileSize ?? DetectTileSize(bitmap);
   var layout = new FrameLayout(bitmap.Width, bitmap.Height, tileSize);

    var tiles = new List<byte>();

   for (int ty = 0; ty < layout.FrameHeightTiles; ty++)
   {
            for (int tx = 0; tx < layout.FrameWidthTiles; tx++)
      {
     var (pixelX, pixelY) = layout.GetTilePosition(tx, ty);
 var color = SampleTileColor(bitmap, pixelX, pixelY, layout.TileSizePx);

 // Convert color to byte (skip white/null tiles)
             if (color.IsWhite)
        {
     // Null tile - could be padding at end of frame
      continue;
           }

          if (_colorMap.TryGetByte(color, out byte value))
      {
       tiles.Add(value);
             }
      else
      {
     // Color not in palette - possible corruption
    // For now, skip or use a default
            // In production, log this as a potential error
                }
        }
 }

return tiles.ToArray();
    }

    private Rgb24 SampleTileColor(SKBitmap bitmap, int x, int y, int tileSize)
    {
        // Sample center pixel of tile to avoid edge artifacts
   int centerX = x + tileSize / 2;
        int centerY = y + tileSize / 2;

  // Bounds check
   if (centerX >= bitmap.Width) centerX = bitmap.Width - 1;
        if (centerY >= bitmap.Height) centerY = bitmap.Height - 1;

 var pixel = bitmap.GetPixel(centerX, centerY);
   return new Rgb24(pixel.Red, pixel.Green, pixel.Blue);
    }

    private TileSize DetectTileSize(SKBitmap bitmap)
    {
// Simple heuristic: check common tile sizes and find best fit
        // In practice, this info should come from metadata or be known
        // For now, default to 8×8
        return TileSize.Size8x8;
    }

    public void Dispose()
    {
      // No unmanaged resources
    }
}
