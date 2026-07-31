using System.Runtime.InteropServices.WindowsRuntime;
using SkiaSharp;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace FluxRead.Interop;

/// <summary>
/// On-device OCR (Windows.Media.Ocr — runs locally, no network) that finds the sender's Next
/// button by reading its "Next" label out of a captured image.
/// </summary>
public static class OcrNextLocator
{
    /// <summary>
    /// Returns the center of the word "Next" found in <paramref name="image"/>, offset by
    /// (<paramref name="originX"/>, <paramref name="originY"/>), or null when OCR is unavailable
    /// or no match is found. Never throws — callers fall back to manual calibration.
    /// </summary>
    public static async Task<(int X, int Y)?> FindNextAsync(SKBitmap image, int originX, int originY)
    {
        try
        {
            var engine = OcrEngine.TryCreateFromUserProfileLanguages()
                ?? OcrEngine.TryCreateFromLanguage(new Language("en-US"));
            if (engine is null)
                return null;

            using var software = ToSoftwareBitmap(image);
            var result = await engine.RecognizeAsync(software);

            foreach (var line in result.Lines)
            {
                foreach (var word in line.Words)
                {
                    if (!word.Text.Trim().StartsWith("Next", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var rect = word.BoundingRect;
                    return (originX + (int)(rect.X + rect.Width / 2), originY + (int)(rect.Y + rect.Height / 2));
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static SoftwareBitmap ToSoftwareBitmap(SKBitmap bitmap)
    {
        var bytes = bitmap.GetPixelSpan().ToArray();
        return SoftwareBitmap.CreateCopyFromBuffer(
            bytes.AsBuffer(), BitmapPixelFormat.Bgra8, bitmap.Width, bitmap.Height, BitmapAlphaMode.Premultiplied);
    }
}
