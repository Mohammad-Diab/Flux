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
    }

    /// <summary>Determines whether the pixel at the given coordinates is dark (below the threshold).</summary>
    /// <param name="x">Pixel column.</param>
    /// <param name="y">Pixel row.</param>
    public bool IsDark(int x, int y) => _pixels[y * Width + x] < Threshold;

    /// <summary>Computes the Rec. 601 luma of an RGB color.</summary>
    public static double Rec601Luma(double r, double g, double b) => 0.299 * r + 0.587 * g + 0.114 * b;

    /// <summary>Extracts the luma channel (Rec. 601 weights) from a bitmap.</summary>
    /// <param name="bitmap">Source bitmap.</param>
    public static LumaImage FromBitmap(SKBitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        var colors = bitmap.Pixels;
        var luma = new byte[colors.Length];
        for (int i = 0; i < colors.Length; i++)
        {
            var c = colors[i];
            luma[i] = (byte)Rec601Luma(c.Red, c.Green, c.Blue);
        }

        return new LumaImage(luma, bitmap.Width, bitmap.Height);
    }
}
