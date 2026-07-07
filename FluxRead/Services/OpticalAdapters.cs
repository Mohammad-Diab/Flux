using System.Windows;
using FluxCore.Transfer;
using FluxRead.Interop;
using SkiaSharp;

namespace FluxRead.Services;

/// <summary>
/// Captures a physical-pixel region for the optical loop; the region is mutable so a stall
/// adjustment can re-point the running loop.
/// </summary>
public sealed class RegionScreenCapture : IScreenCapture
{
    private readonly ScreenRegionCapture _capture = new();

    public Int32Rect Region { get; set; }

    public RegionScreenCapture(Int32Rect region) => Region = region;

    /// <inheritdoc/>
    public SKBitmap Capture() => _capture.Capture(Region);
}

/// <summary>
/// Clicks a calibrated physical-pixel point; mutable so a stall recalibration can retarget
/// the running loop.
/// </summary>
public sealed class PointNextClicker : INextClicker
{
    public (int X, int Y) Point { get; set; }

    public PointNextClicker((int X, int Y) point) => Point = point;

    /// <inheritdoc/>
    public void ClickNext() => MouseClicker.ClickAt(Point.X, Point.Y);
}
