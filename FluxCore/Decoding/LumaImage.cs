using SkiaSharp;

namespace FluxCore.Decoding;

/// <summary>
/// Grayscale view of a captured image with a bimodal contrast threshold, used for
/// fiducial detection and structural (black/white) tile checks.
/// </summary>
public sealed class LumaImage
{
    private readonly byte[] _pixels;

    /// <summary>Gets the image width in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the image height in pixels.</summary>
    public int Height { get; }

    /// <summary>Gets the black/white threshold: midway between the darkest and brightest pixel.</summary>
    public byte Threshold { get; }

    /// <summary>Gets the luma range (brightest minus darkest); a near-zero range means no frame can be present.</summary>
    public int Contrast { get; }

    /// <summary>Wraps row-major luma values of the given dimensions.</summary>
    public LumaImage(byte[] pixels, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (width <= 0 || height <= 0 || pixels.Length != width * height)
            throw new ArgumentException("Pixel buffer does not match the given dimensions.");

        _pixels = pixels;
        Width = width;
        Height = height;

        byte min = 255;
        byte max = 0;
        foreach (var value in pixels)
        {
            if (value < min) min = value;
            if (value > max) max = value;
        }

        Threshold = (byte)((min + max) / 2);
        Contrast = max - min;
    }

    /// <summary>Determines whether the pixel at the given coordinates is dark (below the threshold).</summary>
    /// <param name="x">Pixel column.</param>
    /// <param name="y">Pixel row.</param>
    public bool IsDark(int x, int y) => _pixels[y * Width + x] < Threshold;

    /// <summary>Gets one row of luma values, for scan loops too hot for per-pixel indexing.</summary>
    /// <param name="y">Pixel row.</param>
    public ReadOnlySpan<byte> GetRow(int y) => _pixels.AsSpan(y * Width, Width);

    /// <summary>Computes the Rec. 601 luma of an RGB color.</summary>
    public static double Rec601Luma(double r, double g, double b) => 0.299 * r + 0.587 * g + 0.114 * b;

    /// <summary>Extracts the luma channel (Rec. 601 weights) from a bitmap.</summary>
    /// <param name="bitmap">Source bitmap.</param>
    public static LumaImage FromBitmap(SKBitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        int width = bitmap.Width, height = bitmap.Height;
        var luma = new byte[width * height];

        // Read the pixel buffer directly: bitmap.Pixels would allocate an SKColor[] copy of the
        // whole capture on every poll. Integer weights sum to 256, so >>8 normalizes exactly.
        if (bitmap.ColorType is SKColorType.Bgra8888 or SKColorType.Rgba8888 && bitmap.BytesPerPixel == 4)
        {
            var span = bitmap.GetPixelSpan();
            int rowBytes = bitmap.RowBytes;
            int redOffset = bitmap.ColorType == SKColorType.Bgra8888 ? 2 : 0;
            int blueOffset = 2 - redOffset;
            for (int y = 0; y < height; y++)
            {
                var row = span.Slice(y * rowBytes, width * 4);
                int outIndex = y * width;
                for (int x = 0; x < width; x++)
                {
                    int p = x * 4;
                    luma[outIndex + x] = (byte)((row[p + redOffset] * 77 + row[p + 1] * 150 + row[p + blueOffset] * 29) >> 8);
                }
            }
        }
        else
        {
            var colors = bitmap.Pixels;
            for (int i = 0; i < colors.Length; i++)
            {
                var c = colors[i];
                luma[i] = (byte)Rec601Luma(c.Red, c.Green, c.Blue);
            }
        }

        return new LumaImage(luma, width, height);
    }
}
