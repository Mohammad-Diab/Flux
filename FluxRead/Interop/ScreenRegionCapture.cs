using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using SkiaSharp;

namespace FluxRead.Interop;

/// <summary>
/// Captures a rectangular region of the screen (physical, virtual-desktop coordinates) into an
/// <see cref="SKBitmap"/> the decoder can consume directly. Uses GDI BitBlt via
/// <see cref="Graphics.CopyFromScreen(int, int, int, int, System.Drawing.Size)"/> — synchronous,
/// fast for sub-screen regions, and correct across multiple monitors.
/// </summary>
public sealed class ScreenRegionCapture
{
    /// <summary>
    /// Captures the given physical-pixel region into a BGRA <see cref="SKBitmap"/>.
    /// </summary>
    /// <param name="region">Region in physical, virtual-desktop pixels.</param>
    public SKBitmap Capture(Int32Rect region)
    {
        if (region.Width <= 0 || region.Height <= 0)
            throw new ArgumentException("Capture region must have positive size.", nameof(region));

        using var bitmap = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(
                region.X, region.Y, 0, 0,
                new System.Drawing.Size(region.Width, region.Height),
                CopyPixelOperation.SourceCopy);
        }

        return ToSkBitmap(bitmap);
    }

    private static SKBitmap ToSkBitmap(Bitmap bitmap)
    {
        var info = new SKImageInfo(bitmap.Width, bitmap.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        int rowBytes = bitmap.Width * 4;
        var buffer = new byte[rowBytes * bitmap.Height];

        var data = bitmap.LockBits(
            new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < bitmap.Height; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, buffer, y * rowBytes, rowBytes);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        var skBitmap = new SKBitmap(info);
        Marshal.Copy(buffer, 0, skBitmap.GetPixels(), buffer.Length);
        return skBitmap;
    }
}
