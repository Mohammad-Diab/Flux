using FluxCore.Framing;
using FluxCore.Imaging;
using SkiaSharp;

namespace FluxCore.Decoding;

/// <summary>A located frame's bounding box in image pixels, with its decoded id when readable.</summary>
public readonly record struct FrameRegion(int X, int Y, int Width, int Height, uint? FrameId);

/// <summary>
/// Finds every FFv2 frame in a large image (e.g. a full screenshot): pairs up finder-pattern
/// centers into candidate frame quads by their expected tile spacing, then confirms each by
/// cropping it and requiring the decoder to register it (fiducials + timing match). Registration
/// rejects lookalike patterns, so the returned regions are real frames, not false positives.
/// </summary>
public sealed class FrameLocator
{
    private const double SpanToleranceTiles = 10;
    private const int MaxRegions = 16;

    private readonly FrameDecoder _decoder;

    public FrameLocator(ColorMap colorMap) => _decoder = new FrameDecoder(colorMap);

    /// <summary>Locates frames in the image, largest-first, de-duplicating overlapping candidates.</summary>
    public IReadOnlyList<FrameRegion> Locate(SKBitmap image)
    {
        ArgumentNullException.ThrowIfNull(image);

        var points = FiducialDetector.DetectAll(LumaImage.FromBitmap(image));
        var regions = new List<FrameRegion>();

        foreach (var box in CandidateBoxes(points, image.Width, image.Height))
        {
            if (regions.Any(r => Overlaps(r, box)))
                continue;

            using var crop = new SKBitmap();
            if (!image.ExtractSubset(crop, new SKRectI(box.X, box.Y, box.X + box.Width, box.Y + box.Height)))
                continue;

            var probe = _decoder.TryProbe(crop);
            if (probe.Registered)
            {
                regions.Add(box with { FrameId = probe.Header?.FrameId });
                if (regions.Count >= MaxRegions)
                    break;
            }
        }

        return regions;
    }

    private static IEnumerable<FrameRegion> CandidateBoxes(IReadOnlyList<FinderPoint> points, int imageWidth, int imageHeight)
    {
        var centers = FrameFormat.FinderCentersTiles;
        double hSpanTiles = centers[1].X - centers[0].X;
        double vSpanTiles = centers[2].Y - centers[0].Y;
        double marginTiles = centers[0].X + FrameFormat.QuietZonePx / (double)FrameFormat.TilePixelSize;
        double frameTilesW = FrameFormat.FrameWidthPx / (double)FrameFormat.TilePixelSize;
        double frameTilesH = FrameFormat.FrameHeightPx / (double)FrameFormat.TilePixelSize;

        for (int i = 0; i < points.Count; i++)
        {
            for (int j = 0; j < points.Count; j++)
            {
                if (i == j)
                    continue;

                FinderPoint tl = points[i], tr = points[j];
                double module = (tl.ModuleSize + tr.ModuleSize) / 2;
                if (module <= 0 || Math.Abs(tr.Y - tl.Y) > module)
                    continue;
                if (Math.Abs((tr.X - tl.X) / module - hSpanTiles) > SpanToleranceTiles)
                    continue;

                if (Nearest(points, tl.X, tl.Y + vSpanTiles * module, module) is null ||
                    Nearest(points, tr.X, tr.Y + vSpanTiles * module, module) is null)
                    continue;

                int x = (int)Math.Round(tl.X - marginTiles * module);
                int y = (int)Math.Round(tl.Y - marginTiles * module);
                if (Clamp(x, y, (int)Math.Round(frameTilesW * module), (int)Math.Round(frameTilesH * module),
                        imageWidth, imageHeight) is { } box)
                    yield return box;
            }
        }
    }

    private static FinderPoint? Nearest(IReadOnlyList<FinderPoint> points, double x, double y, double module)
    {
        foreach (var p in points)
        {
            if (Math.Abs(p.X - x) <= module && Math.Abs(p.Y - y) <= module)
                return p;
        }

        return null;
    }

    private static FrameRegion? Clamp(int x, int y, int width, int height, int imageWidth, int imageHeight)
    {
        int x0 = Math.Max(0, x), y0 = Math.Max(0, y);
        int x1 = Math.Min(imageWidth, x + width), y1 = Math.Min(imageHeight, y + height);
        return x1 - x0 >= FrameFormat.TilePixelSize && y1 - y0 >= FrameFormat.TilePixelSize
            ? new FrameRegion(x0, y0, x1 - x0, y1 - y0, null)
            : null;
    }

    private static bool Overlaps(FrameRegion a, FrameRegion b)
    {
        int ix = Math.Max(0, Math.Min(a.X + a.Width, b.X + b.Width) - Math.Max(a.X, b.X));
        int iy = Math.Max(0, Math.Min(a.Y + a.Height, b.Y + b.Height) - Math.Max(a.Y, b.Y));
        int minArea = Math.Min(a.Width * a.Height, b.Width * b.Height);
        return minArea > 0 && ix * iy > minArea / 2;
    }
}
