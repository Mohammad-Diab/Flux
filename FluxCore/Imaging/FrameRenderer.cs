using FluxCore.Framing;
using SkiaSharp;

namespace FluxCore.Imaging;

/// <summary>
/// Renders a <see cref="FrameTileMap"/> to the canonical 1312x752 PNG: white quiet zone,
/// 8x8-pixel tiles, no antialiasing so every pixel is an exact palette or structural color.
/// </summary>
public static class FrameRenderer
{
    private static readonly SKColor Black = new(0, 0, 0);

    /// <summary>
    /// Renders the frame to PNG bytes at canonical scale.
    /// </summary>
    /// <param name="tiles">Complete tile map of the frame.</param>
    /// <param name="colorMap">Color map for data and header tiles.</param>
    public static byte[] RenderPng(FrameTileMap tiles, ColorMap colorMap)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        ArgumentNullException.ThrowIfNull(colorMap);

        using var bitmap = new SKBitmap(
            FrameFormat.FrameWidthPx, FrameFormat.FrameHeightPx, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);

        using var paint = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = false };

        for (int y = 0; y < FrameFormat.GridHeightTiles; y++)
        {
            for (int x = 0; x < FrameFormat.GridWidthTiles; x++)
            {
                if (!TryGetTileColor(tiles, colorMap, x, y, out var color))
                    continue;

                paint.Color = color;
                var (px, py) = FrameFormat.TileToPixel(x, y);
                canvas.DrawRect((float)px, (float)py, FrameFormat.TilePixelSize, FrameFormat.TilePixelSize, paint);
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static bool TryGetTileColor(FrameTileMap tiles, ColorMap colorMap, int x, int y, out SKColor color)
    {
        switch (FrameFormat.GetRole(x, y))
        {
            case TileRole.Finder:
            case TileRole.Timing:
                if (!FrameFormat.IsStructuralBlack(x, y))
                    break;
                color = Black;
                return true;

            case TileRole.Beacon:
                if (!tiles.BeaconIsBlack)
                    break;
                color = Black;
                return true;

            case TileRole.Data:
            case TileRole.Header:
                var value = tiles.GetTileValue(x, y);
                var rgb = tiles.ColorScheme == TileColorScheme.CubeCorner8
                    ? CubeCornerColors.ToColor(value)
                    : colorMap.GetColor(value);
                color = new SKColor(rgb.R, rgb.G, rgb.B);
                return true;
        }

        color = SKColors.White;
        return false;
    }
}
