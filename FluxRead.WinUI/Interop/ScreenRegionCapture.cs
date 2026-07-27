using System.Runtime.InteropServices;
using SkiaSharp;
using Windows.Graphics;

namespace FluxRead.WinUI.Interop;

/// <summary>
/// Captures a rectangular region of the screen (physical, virtual-desktop coordinates) into an
/// <see cref="SKBitmap"/> the decoder can consume directly. GDI <c>BitBlt</c> into a top-down 32-bit
/// DIB — the WPF app's <c>Graphics.CopyFromScreen</c> without a System.Drawing dependency.
/// </summary>
public sealed class ScreenRegionCapture
{
    /// <summary>Captures the given physical-pixel region into a BGRA <see cref="SKBitmap"/>.</summary>
    /// <param name="region">Region in physical, virtual-desktop pixels.</param>
    public SKBitmap Capture(RectInt32 region)
    {
        if (region.Width <= 0 || region.Height <= 0)
            throw new ArgumentException("Capture region must have positive size.", nameof(region));

        var screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
            throw new InvalidOperationException("Could not get the screen device context.");

        var memoryDc = CreateCompatibleDC(screenDc);
        var header = new BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = region.Width,
            biHeight = -region.Height,   // negative: top-down rows, so they match SKBitmap's order
            biPlanes = 1,
            biBitCount = 32,
            biCompression = BiRgb,
        };

        var dib = CreateDIBSection(memoryDc, ref header, DibRgbColors, out var pixels, IntPtr.Zero, 0);
        var previous = SelectObject(memoryDc, dib);

        try
        {
            if (dib == IntPtr.Zero || pixels == IntPtr.Zero)
                throw new InvalidOperationException("Could not allocate the capture bitmap.");

            if (!BitBlt(memoryDc, 0, 0, region.Width, region.Height, screenDc, region.X, region.Y, SrcCopy))
                throw new InvalidOperationException("BitBlt failed for the capture region.");

            return ToSkBitmap(pixels, region.Width, region.Height);
        }
        finally
        {
            SelectObject(memoryDc, previous);
            if (dib != IntPtr.Zero)
                DeleteObject(dib);
            DeleteDC(memoryDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private static SKBitmap ToSkBitmap(IntPtr pixels, int width, int height)
    {
        int length = width * height * 4;
        var buffer = new byte[length];
        Marshal.Copy(pixels, buffer, 0, length);

        // BitBlt leaves the alpha byte at whatever was on screen (usually 0), which a premultiplied
        // bitmap would read back as black.
        for (int i = 3; i < length; i += 4)
            buffer[i] = 0xFF;

        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        Marshal.Copy(buffer, 0, bitmap.GetPixels(), length);
        return bitmap;
    }

    private const int BiRgb = 0;
    private const uint DibRgbColors = 0;
    private const uint SrcCopy = 0x00CC0020;

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize;
        public int biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter;
        public int biClrUsed, biClrImportant;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(
        IntPtr dc, ref BITMAPINFOHEADER header, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(
        IntPtr dest, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, uint rop);
}
