using System.Windows;
using FluxCore.Transfer;
using FluxRead.Interop;
using SkiaSharp;

namespace FluxRead.Services;

/// <summary>
/// Captures a fixed physical-pixel region for the optical loop.
/// </summary>
public sealed class RegionScreenCapture : IScreenCapture
{
    private readonly ScreenRegionCapture _capture = new();
    private readonly Int32Rect _region;

    public RegionScreenCapture(Int32Rect region) => _region = region;

    /// <inheritdoc/>
    public SKBitmap Capture() => _capture.Capture(_region);
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
