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

    /// <summary>
    /// Initializes a new instance of the <see cref="RegionScreenCapture"/> class.
    /// </summary>
    /// <param name="region">Capture region in physical pixels.</param>
    public RegionScreenCapture(Int32Rect region) => _region = region;

    /// <inheritdoc/>
    public SKBitmap Capture() => _capture.Capture(_region);
}

/// <summary>
/// Clicks a calibrated physical-pixel point for the optical loop. The point is mutable so a
/// stall recalibration can update it without recreating the loop.
/// </summary>
public sealed class PointNextClicker : INextClicker
{
    /// <summary>Gets or sets the Next-button point in physical pixels.</summary>
    public (int X, int Y) Point { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PointNextClicker"/> class.
    /// </summary>
    /// <param name="point">Initial Next-button point in physical pixels.</param>
    public PointNextClicker((int X, int Y) point) => Point = point;

    /// <inheritdoc/>
    public void ClickNext() => MouseClicker.ClickAt(Point.X, Point.Y);
}
