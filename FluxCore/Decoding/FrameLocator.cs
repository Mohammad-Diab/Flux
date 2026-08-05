using FluxCore.Framing;
using FluxCore.Imaging;
using SkiaSharp;

namespace FluxCore.Decoding;

/// <summary>A located frame's bounding box in image pixels, with its decoded id when readable.</summary>
public readonly record struct FrameRegion(int X, int Y, int Width, int Height, uint? FrameId);

/// <summary>
/// Finds every frame of a known layout in a large image (e.g. a full screenshot): pairs up
/// finder-pattern centers into candidate quads by the layout's tile spacing, then confirms each
/// by cropping it and requiring the decoder to register it (fiducials + timing match).
/// Registration rejects lookalike patterns, so the returned regions are real frames, not false
/// positives. Defaults to the bootstrap layout (frame 0); pass the transfer's adopted layout as
/// well to re-find payload frames mid-transfer.
/// </summary>
public sealed class FrameLocator
{
    private const double SpanToleranceTiles = 10;
    private const int MaxRegions = 16;

    private readonly FrameDecoder _decoder;

    /// <summary>Creates a locator; the palette only affects reading frame ids, never detection.</summary>
    public FrameLocator(ColorMap colorMap) => _decoder = new FrameDecoder(colorMap);

    /// <summary>Locates frames in the image, largest-first, de-duplicating overlapping candidates.</summary>
    /// <param name="image">Image to search.</param>
    /// <param name="layouts">Frame layouts to look for; earlier layouts win overlaps. Null means the bootstrap layout.</param>
    public IReadOnlyList<FrameRegion> Locate(SKBitmap image, IReadOnlyList<FrameLayout>? layouts = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        layouts ??= [BootstrapFrame.Layout];

        var points = FiducialDetector.DetectAll(LumaImage.FromBitmap(image));
        var regions = new List<FrameRegion>();

        foreach (var layout in layouts.Distinct())
        {
            foreach (var box in CandidateBoxes(points, image.Width, image.Height, layout))
            {
                if (regions.Any(r => Overlaps(r, box)))
                    continue;

                using var crop = new SKBitmap();
                if (!image.ExtractSubset(crop, new SKRectI(box.X, box.Y, box.X + box.Width, box.Y + box.Height)))
                    continue;

                var probe = _decoder.TryProbe(crop, layout);
                if (probe.Registered)
                {
                    regions.Add(box with { FrameId = probe.Header?.FrameId });
                    if (regions.Count >= MaxRegions)
                        return regions;
                }
            }
        }

        return regions;
    }

    private static IEnumerable<FrameRegion> CandidateBoxes(
        IReadOnlyList<FinderPoint> points, int imageWidth, int imageHeight, FrameLayout layout)
    {
        var centers = layout.FinderCentersTiles;
        double hSpanTiles = centers[1].X - centers[0].X;
        double vSpanTiles = centers[2].Y - centers[0].Y;
        double marginTiles = centers[0].X + layout.QuietZonePx / (double)layout.TilePixelSize;
        double frameTilesW = layout.FrameWidthPx / (double)layout.TilePixelSize;
        double frameTilesH = layout.FrameHeightPx / (double)layout.TilePixelSize;

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
