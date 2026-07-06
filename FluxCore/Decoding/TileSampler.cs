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
    public double Luma => LumaImage.Rec601Luma(R, G, B);
}

/// <summary>
/// Samples tile colors from a captured image: maps each tile center through the homography
/// and averages a 3x3 pattern of bilinear sub-pixel samples offset by ~22% of the projected
/// tile pitch, so samples stay strictly interior to the tile at any capture scale and
/// integer-rounding error never contaminates the mean.
/// </summary>
public sealed class TileSampler
{
    private const double OffsetFraction = 0.22;

    private readonly SKColor[] _pixels;
    private readonly int _width;
    private readonly int _height;
    private readonly Homography _tileToImage;
    private readonly double _offset;

    /// <summary>Creates a sampler for a capture and its tile-space-to-image homography.</summary>
    public TileSampler(SKBitmap bitmap, Homography tileToImage)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        ArgumentNullException.ThrowIfNull(tileToImage);

        _pixels = bitmap.Pixels;
        _width = bitmap.Width;
        _height = bitmap.Height;
        _tileToImage = tileToImage;

        // Projected tile pitch in captured-image pixels, measured at the grid center.
        var a = tileToImage.Map(80.5, 45.5);
        var b = tileToImage.Map(81.5, 45.5);
        double pitch = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
        _offset = OffsetFraction * pitch;
    }

    /// <summary>Samples the tile at the given grid coordinates.</summary>
    /// <param name="tileX">Tile column (0-159).</param>
    /// <param name="tileY">Tile row (0-89).</param>
    public TileSample Sample(int tileX, int tileY)
    {
        var (cx, cy) = _tileToImage.Map(tileX + 0.5, tileY + 0.5);

        double r = 0, g = 0, b = 0;

        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                var (sr, sg, sb) = SampleBilinear(cx + dx * _offset, cy + dy * _offset);
                r += sr;
                g += sg;
                b += sb;
            }
        }

        return new TileSample(r / 9, g / 9, b / 9);
    }

    private (double R, double G, double B) SampleBilinear(double x, double y)
    {
        double clampedX = Math.Clamp(x - 0.5, 0, _width - 1);
        double clampedY = Math.Clamp(y - 0.5, 0, _height - 1);

        int x0 = (int)clampedX;
        int y0 = (int)clampedY;
        int x1 = Math.Min(x0 + 1, _width - 1);
        int y1 = Math.Min(y0 + 1, _height - 1);
        double fx = clampedX - x0;
        double fy = clampedY - y0;

        var c00 = _pixels[y0 * _width + x0];
        var c10 = _pixels[y0 * _width + x1];
        var c01 = _pixels[y1 * _width + x0];
        var c11 = _pixels[y1 * _width + x1];

        double w00 = (1 - fx) * (1 - fy);
        double w10 = fx * (1 - fy);
        double w01 = (1 - fx) * fy;
        double w11 = fx * fy;

        return (
            c00.Red * w00 + c10.Red * w10 + c01.Red * w01 + c11.Red * w11,
            c00.Green * w00 + c10.Green * w10 + c01.Green * w01 + c11.Green * w11,
            c00.Blue * w00 + c10.Blue * w10 + c01.Blue * w01 + c11.Blue * w11);
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
