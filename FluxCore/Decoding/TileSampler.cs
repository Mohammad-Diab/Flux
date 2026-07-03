using FluxCore.Framing;
using SkiaSharp;

namespace FluxCore.Decoding;

/// <summary>
/// Mean RGB of a sampled tile neighborhood.
/// </summary>
/// <param name="R">Mean red component.</param>
/// <param name="G">Mean green component.</param>
/// <param name="B">Mean blue component.</param>
public readonly record struct TileSample(double R, double G, double B)
{
    /// <summary>Gets the Rec. 601 luma of the sample.</summary>
    public double Luma => 0.299 * R + 0.587 * G + 0.114 * B;
}

/// <summary>
/// Samples tile colors from a captured image: maps each tile center through the homography
/// and averages a square neighborhood scaled to ~30% of the projected tile pitch, so slight
/// registration error and edge bleed do not contaminate the sample.
/// </summary>
public sealed class TileSampler
{
    private readonly SKColor[] _pixels;
    private readonly int _width;
    private readonly int _height;
    private readonly Homography _tileToImage;
    private readonly int _radius;

    /// <summary>Gets the projected tile pitch in captured-image pixels.</summary>
    public double Pitch { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="TileSampler"/> class.
    /// </summary>
    /// <param name="bitmap">Captured image.</param>
    /// <param name="tileToImage">Homography from tile space to image pixels.</param>
    public TileSampler(SKBitmap bitmap, Homography tileToImage)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        ArgumentNullException.ThrowIfNull(tileToImage);

        _pixels = bitmap.Pixels;
        _width = bitmap.Width;
        _height = bitmap.Height;
        _tileToImage = tileToImage;

        var a = tileToImage.Map(80.5, 45.5);
        var b = tileToImage.Map(81.5, 45.5);
        Pitch = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
        _radius = Math.Max(1, (int)Math.Round(0.3 * Pitch));
    }

    /// <summary>Samples the tile at the given grid coordinates.</summary>
    /// <param name="tileX">Tile column (0-159).</param>
    /// <param name="tileY">Tile row (0-89).</param>
    public TileSample Sample(int tileX, int tileY)
    {
        var (cx, cy) = _tileToImage.Map(tileX + 0.5, tileY + 0.5);
        int centerX = (int)Math.Round(cx);
        int centerY = (int)Math.Round(cy);

        double r = 0, g = 0, b = 0;
        int count = 0;

        for (int dy = -_radius; dy <= _radius; dy++)
        {
            int py = centerY + dy;
            if (py < 0 || py >= _height)
                continue;

            for (int dx = -_radius; dx <= _radius; dx++)
            {
                int px = centerX + dx;
                if (px < 0 || px >= _width)
                    continue;

                var color = _pixels[py * _width + px];
                r += color.Red;
                g += color.Green;
                b += color.Blue;
                count++;
            }
        }

        if (count == 0)
            return new TileSample(255, 255, 255);

        return new TileSample(r / count, g / count, b / count);
    }

    /// <summary>Samples every tile in the grid, returned row-major (14,400 entries).</summary>
    public TileSample[] SampleAll()
    {
        var samples = new TileSample[FrameFormat.TotalTiles];
        for (int y = 0; y < FrameFormat.GridHeightTiles; y++)
        {
            for (int x = 0; x < FrameFormat.GridWidthTiles; x++)
            {
                samples[y * FrameFormat.GridWidthTiles + x] = Sample(x, y);
            }
        }

        return samples;
    }
}
