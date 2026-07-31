using Microsoft.UI.Xaml.Media.Imaging;
using SkiaSharp;
using Windows.Storage.Streams;

namespace Flux.Ui.Services;

/// <summary>Converts capture output to WinUI image sources.</summary>
public static class BitmapConverter
{
    public static async Task<BitmapImage> ToImageSourceAsync(SKBitmap bitmap, int quality = 80)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, quality);
        return await FromPngAsync(data.ToArray());
    }

    public static async Task<BitmapImage> FromPngAsync(byte[] png)
    {
        var stream = new InMemoryRandomAccessStream();
        var writer = new DataWriter(stream);
        writer.WriteBytes(png);
        await writer.StoreAsync();
        // Detach first: disposing the writer would close the stream the image still reads from.
        writer.DetachStream();
        writer.Dispose();

        stream.Seek(0);
        var source = new BitmapImage();
        await source.SetSourceAsync(stream);
        return source;
    }
}
