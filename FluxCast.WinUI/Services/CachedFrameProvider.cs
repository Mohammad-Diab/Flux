using System.IO;
using FluxCore.Transfer;
using Microsoft.UI.Xaml.Media.Imaging;

namespace FluxCast.WinUI.Services;

/// <summary>
/// Loads frame PNGs as bitmaps with a small cache around the current frame. WinUI decodes on the UI
/// thread asynchronously, so neighbours are pre-created rather than pre-decoded on a worker.
/// </summary>
public sealed class CachedFrameProvider
{
    private const int CacheRadius = 3;

    private readonly string _framesDirectory;
    private readonly uint _totalFrames;
    private readonly Dictionary<int, BitmapImage> _cache = [];

    public CachedFrameProvider(string framesDirectory, uint totalFrames)
    {
        _framesDirectory = framesDirectory;
        _totalFrames = totalFrames;
    }

    /// <summary>Gets the frame at the given index, warming its neighbours for Next and Back.</summary>
    public BitmapImage GetFrame(int index)
    {
        var frame = GetOrLoad(index);

        for (int offset = 1; offset <= CacheRadius; offset++)
        {
            if (index + offset < _totalFrames)
                GetOrLoad(index + offset);
            if (index - offset >= 0)
                GetOrLoad(index - offset);
        }

        Trim(index);
        return frame;
    }

    private BitmapImage GetOrLoad(int index)
    {
        if (_cache.TryGetValue(index, out var cached))
            return cached;

        var path = Path.Combine(_framesDirectory, FluxEncodeService.FrameFileName((uint)index));
        // A frame must never be resampled, so no DecodePixelWidth: it decodes at its native size.
        var bitmap = new BitmapImage(new Uri(path));
        _cache[index] = bitmap;
        return bitmap;
    }

    private void Trim(int center)
    {
        foreach (var key in _cache.Keys.Where(k => Math.Abs(k - center) > CacheRadius).ToList())
            _cache.Remove(key);
    }
}
