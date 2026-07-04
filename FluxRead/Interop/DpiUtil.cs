using System.Windows;
using System.Windows.Media;

namespace FluxRead.Interop;

/// <summary>
/// Converts between WPF device-independent pixels (DIPs) and physical screen pixels, using
/// the DPI of the specific monitor a point falls on. Region selection and click calibration
/// work in physical pixels because screen capture and <c>SendInput</c> operate there.
/// </summary>
public static class DpiUtil
{
    /// <summary>Gets the DPI scale (1.0 = 96 DPI, 1.5 = 144 DPI) of the monitor under a physical point.</summary>
    /// <param name="physicalX">Physical X coordinate.</param>
    /// <param name="physicalY">Physical Y coordinate.</param>
    public static double GetScaleForPhysicalPoint(int physicalX, int physicalY)
    {
        var point = new NativeMethods.POINT { X = physicalX, Y = physicalY };
        var monitor = NativeMethods.MonitorFromPoint(point, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0)
            return dpiX / 96.0;

        return 1.0;
    }

    /// <summary>Maps a point in a visual's DIP space to physical screen pixels.</summary>
    /// <param name="visual">A visual attached to a rendered window.</param>
    /// <param name="dipPoint">Point in the visual's DIP coordinate space (screen-relative DIPs).</param>
    public static (int X, int Y) DipToPhysical(Visual visual, Point dipPoint)
    {
        var source = PresentationSource.FromVisual(visual)
            ?? throw new InvalidOperationException("Visual is not connected to a rendered window.");
        var transform = source.CompositionTarget.TransformToDevice;
        var device = transform.Transform(dipPoint);
        return ((int)Math.Round(device.X), (int)Math.Round(device.Y));
    }

    /// <summary>Maps a screen-space DIP rectangle to a physical-pixel rectangle.</summary>
    /// <param name="visual">A visual attached to a rendered window.</param>
    /// <param name="dipRect">Rectangle in screen DIP coordinates.</param>
    public static Int32Rect DipRectToPhysical(Visual visual, Rect dipRect)
    {
        var (leftX, topY) = DipToPhysical(visual, dipRect.TopLeft);
        var (rightX, bottomY) = DipToPhysical(visual, dipRect.BottomRight);
        return new Int32Rect(leftX, topY, Math.Max(0, rightX - leftX), Math.Max(0, bottomY - topY));
    }

    /// <summary>Gets the virtual screen bounds (spanning all monitors) in physical pixels.</summary>
    public static Int32Rect GetVirtualScreenPhysical() => new(
        NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN),
        NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN),
        NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN),
        NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN));
}
