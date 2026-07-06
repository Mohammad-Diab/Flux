using System.IO;
using System.Windows.Media.Imaging;
using FluxCore.Transfer;

namespace FluxCast.Services;

/// <summary>
/// Loads frame PNGs as frozen bitmaps with a small cache around the current frame,
/// prefetching neighbors in the background so Next and Back respond instantly.
/// </summary>
public sealed class CachedFrameProvider
{
    private const int CacheRadius = 3;

    private readonly string _framesDirectory;
    private readonly uint _totalFrames;
    private readonly Dictionary<int, BitmapSource> _cache = new();
    private readonly object _gate = new();

    public CachedFrameProvider(string framesDirectory, uint totalFrames)
    {
        _framesDirectory = framesDirectory;
        _totalFrames = totalFrames;
    }

    /// <summary>Gets the frame at the given index and kicks off neighbor prefetch.</summary>
    public BitmapSource GetFrame(int index)
    {
        var frame = GetOrLoad(index);

        _ = Task.Run(() =>
        {
            for (int offset = 1; offset <= CacheRadius; offset++)
            {
                if (index + offset < _totalFrames)
                    GetOrLoad(index + offset);
                if (index - offset >= 0)
                    GetOrLoad(index - offset);
            }

            Trim(index);
        });

        return frame;
    }

    private BitmapSource GetOrLoad(int index)
    {
        lock (_gate)
        {
            if (_cache.TryGetValue(index, out var cached))
                return cached;
        }

        var path = Path.Combine(_framesDirectory, FluxEncodeService.FrameFileName((uint)index));
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(path);
        bitmap.EndInit();
        bitmap.Freeze();

        lock (_gate)
        {
            _cache[index] = bitmap;
        }

        return bitmap;
    }

    private void Trim(int center)
    {
        lock (_gate)
        {
            var stale = _cache.Keys.Where(k => Math.Abs(k - center) > CacheRadius).ToList();
            foreach (var key in stale)
            {
                _cache.Remove(key);
            }
        }
    }
}
