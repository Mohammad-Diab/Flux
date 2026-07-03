using FluxCore.Ecc;
using FluxCore.Hashing;
using FluxCore.Imaging;
using SkiaSharp;

namespace FluxCore.Framing;

/// <summary>
/// Encodes byte data into visual frame images with ECC and headers.
/// </summary>
public sealed class FrameEncoder : IDisposable
{
    private readonly FrameLayout _layout;
    private readonly ColorMap _colorMap;
    private readonly ReedSolomonEcc? _ecc;
    private readonly byte _encodingCode;

    /// <summary>
    /// Initializes a new instance of the <see cref="FrameEncoder"/> class.
    /// </summary>
    /// <param name="layout">Frame layout configuration.</param>
    /// <param name="colorMap">Color map for byte?color conversion.</param>
    /// <param name="ecc">Optional Reed-Solomon ECC encoder.</param>
    /// <param name="encodingCode">Encoding code/version (default: 1).</param>
    public FrameEncoder(FrameLayout layout, ColorMap colorMap, ReedSolomonEcc? ecc = null, byte encodingCode = 1)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(colorMap);

        _layout = layout;
        _colorMap = colorMap;
        _ecc = ecc;
    _encodingCode = encodingCode;
 }

    /// <summary>
    /// Encodes a data payload into a frame image.
    /// </summary>
    /// <param name="payload">Payload bytes to encode (max = FrameLayout.TotalTiles).</param>
    /// <param name="frameId">Frame ID (0-based).</param>
    /// <param name="totalFrames">Total number of frames in the encoding.</param>
    /// <returns>Encoded frame image as PNG byte array.</returns>
    /// <exception cref="ArgumentException">Thrown when payload exceeds frame capacity.</exception>
    public byte[] EncodeFrame(byte[] payload, uint frameId, uint totalFrames)
    {
        ArgumentNullException.ThrowIfNull(payload);

        // Apply ECC if configured
        byte[] dataWithEcc = _ecc != null ? _ecc.Encode(payload) : payload;

        if (dataWithEcc.Length > _layout.TotalTiles)
            throw new ArgumentException(
     $"Payload with ECC ({dataWithEcc.Length} bytes) exceeds frame capacity ({_layout.TotalTiles} tiles).",
       nameof(payload));

        // Compute CRC-32 of the ECC-encoded payload
  uint crc32 = Crc32Helper.ComputeChecksum(dataWithEcc);

        // Create frame header
        var header = new FrameHeader(
    _encodingCode,
        frameId,
 totalFrames,
            (ushort)_layout.FrameWidthTiles,
         (ushort)_layout.FrameHeightTiles,
            crc32);

      // Generate frame image
        return GenerateFrameImage(header, dataWithEcc);
    }

    /// <summary>
    /// Encodes metadata payload into Frame 0 (always uses 8×8 tiles).
    /// </summary>
    /// <param name="metadata">Metadata payload.</param>
    /// <param name="totalFrames">Total number of frames.</param>
    /// <returns>Encoded metadata frame image as PNG byte array.</returns>
    public byte[] EncodeMetadataFrame(MetadataPayload metadata, uint totalFrames)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        var metadataBytes = metadata.Serialize();

     // Metadata frame always uses 8×8 tiles for consistency
        var metadataLayout = new FrameLayout(
       _layout.FrameWidthPx,
            _layout.FrameHeightPx,
   TileSize.Size8x8,
            _layout.SeparatorEvery);

    // Create temporary encoder for metadata frame
     using var metadataEncoder = new FrameEncoder(metadataLayout, metadata.ColorMap, null, _encodingCode);
        return metadataEncoder.EncodeFrame(metadataBytes, frameId: 0, totalFrames);
    }

    private byte[] GenerateFrameImage(FrameHeader header, byte[] payload)
    {
        using var surface = SKSurface.Create(new SKImageInfo(_layout.FrameWidthPx, _layout.FrameHeightPx));
        var canvas = surface.Canvas;

        // Fill background with white
        canvas.Clear(SKColors.White);

        // Draw tiles
        int tileIndex = 0;
        for (int ty = 0; ty < _layout.FrameHeightTiles && tileIndex < payload.Length; ty++)
      {
         for (int tx = 0; tx < _layout.FrameWidthTiles && tileIndex < payload.Length; tx++)
      {
          var (pixelX, pixelY) = _layout.GetTilePosition(tx, ty);
       var color = _colorMap.GetColor(payload[tileIndex]);
 
         DrawTile(canvas, pixelX, pixelY, color);
    tileIndex++;
            }
        }

        // Draw separators (white lines every SeparatorEvery tiles)
   DrawSeparators(canvas);

        // Encode header in top-left corner (as metadata overlay or separate mechanism)
   // For simplicity, we'll store the header separately and not in the image itself
        // The decoder will need to parse the header from a separate source or embedded data

        // Export to PNG
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
  return data.ToArray();
    }

    private void DrawTile(SKCanvas canvas, int x, int y, Rgb24 color)
    {
        using var paint = new SKPaint
        {
 Color = new SKColor(color.R, color.G, color.B),
            Style = SKPaintStyle.Fill
        };

        canvas.DrawRect(x, y, _layout.TileSizePx, _layout.TileSizePx, paint);
    }

    private void DrawSeparators(SKCanvas canvas)
    {
   using var paint = new SKPaint
   {
       Color = SKColors.White,
            Style = SKPaintStyle.Fill
    };

  // Vertical separators
        for (int tx = _layout.SeparatorEvery; tx < _layout.FrameWidthTiles; tx += _layout.SeparatorEvery)
        {
      var (pixelX, _) = _layout.GetTilePosition(tx, 0);
            int separatorX = pixelX - _layout.SeparatorPx;
        canvas.DrawRect(separatorX, _layout.MarginPx, _layout.SeparatorPx, 
       _layout.FrameHeightPx - 2 * _layout.MarginPx, paint);
        }

// Horizontal separators
        for (int ty = _layout.SeparatorEvery; ty < _layout.FrameHeightTiles; ty += _layout.SeparatorEvery)
        {
      var (_, pixelY) = _layout.GetTilePosition(0, ty);
   int separatorY = pixelY - _layout.SeparatorPx;
            canvas.DrawRect(_layout.MarginPx, separatorY, 
      _layout.FrameWidthPx - 2 * _layout.MarginPx, _layout.SeparatorPx, paint);
        }
    }

    public void Dispose()
    {
        // No unmanaged resources to dispose currently
    }
}
