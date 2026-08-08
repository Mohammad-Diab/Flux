using FluxCore.Transfer;
using FluxRead.Interop;
using SkiaSharp;
using Windows.Graphics;

namespace FluxRead.Services;

/// <summary>
/// Captures a physical-pixel region for the optical loop; the region is mutable so a stall
/// adjustment can re-point the running loop. While following a window, the region is re-read
/// from the window's rect on every capture, so a moved sender stays captured; setting
/// <see cref="Region"/> explicitly stops following (a manual adjustment wins).
/// </summary>
public sealed class RegionScreenCapture : IScreenCapture
{
    private readonly ScreenRegionCapture _capture = new();
    private RectInt32 _region;
    private IntPtr _follow;

    public RectInt32 Region
    {
        get => _region;
        set { _region = value; _follow = IntPtr.Zero; }
    }

    public RegionScreenCapture(RectInt32 region) => Region = region;

    /// <summary>Glues the region to a window's on-screen rect until <see cref="Region"/> is set explicitly.</summary>
    public void FollowWindow(IntPtr window) => _follow = window;

    /// <inheritdoc/>
    public SKBitmap Capture()
    {
        if (_follow != IntPtr.Zero && NativeMethods.IsWindow(_follow) &&
            NativeMethods.GetWindowRect(_follow, out var box))
        {
            var screen = DpiUtil.GetVirtualScreenPhysical();
            int x = Math.Max(screen.X, box.Left), y = Math.Max(screen.Y, box.Top);
            int right = Math.Min(screen.X + screen.Width, box.Right);
            int bottom = Math.Min(screen.Y + screen.Height, box.Bottom);
            if (right > x && bottom > y)
                _region = new RectInt32(x, y, right - x, bottom - y);
        }

        return _capture.Capture(_region);
    }
}

/// <summary>
/// Clicks the calibrated point only while the sender window still owns it: a covering window is
/// reported instead of clicked, and the point rides the window so a moved sender is followed.
/// Mutable so a stall recalibration can retarget the running loop.
/// </summary>
public sealed class PointNextClicker : INextClicker
{
    private readonly IntPtr _sender;
    private (int X, int Y) _point;
    private (int X, int Y) _offset;

    public (int X, int Y) Point
    {
        get => _point;
        set { _point = value; _offset = ToWindowOffset(value); }
    }

    public PointNextClicker((int X, int Y) point)
    {
        _sender = RootWindowAt(point);
        Point = point;
    }

    /// <inheritdoc/>
    public NextClickOutcome ClickNext()
    {
        if (_sender == IntPtr.Zero)
        {
            // No window resolved at calibration; the unguarded click is all there is.
            MouseClicker.ClickAt(_point.X, _point.Y);
            return NextClickOutcome.Clicked;
        }

        if (!NativeMethods.IsWindow(_sender))
            return NextClickOutcome.WindowGone;
        if (NativeMethods.IsIconic(_sender))
            return NextClickOutcome.Minimized;

        if (NativeMethods.GetWindowRect(_sender, out var box))
            _point = (box.Left + _offset.X, box.Top + _offset.Y);

        if (RootWindowAt(_point) != _sender)
            return NextClickOutcome.Covered;

        MouseClicker.ClickAt(_point.X, _point.Y);
        return NextClickOutcome.Clicked;
    }

    private static IntPtr RootWindowAt((int X, int Y) point) =>
        NativeMethods.GetAncestor(
            NativeMethods.WindowFromPoint(new NativeMethods.POINT { X = point.X, Y = point.Y }),
            NativeMethods.GA_ROOT);

    private (int X, int Y) ToWindowOffset((int X, int Y) point) =>
        _sender != IntPtr.Zero && NativeMethods.GetWindowRect(_sender, out var box)
            ? (point.X - box.Left, point.Y - box.Top)
            : point;
}
