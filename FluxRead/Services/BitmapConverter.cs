using System.IO;
using System.Windows.Media.Imaging;
using SkiaSharp;

namespace FluxRead.Services;

/// <summary>Converts capture output to frozen, binding-ready bitmap sources.</summary>
public static class BitmapConverter
{
    public static BitmapSource ToBitmapSource(SKBitmap bitmap, int quality = 80)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, quality);
        return FromPng(data.ToArray());
    }

    public static BitmapSource FromPng(byte[] png)
    {
        using var stream = new MemoryStream(png);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
