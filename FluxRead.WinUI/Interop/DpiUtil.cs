using FluxRead.Interop;
using Windows.Graphics;

namespace FluxRead.WinUI.Interop;

/// <summary>
/// Physical-pixel screen geometry. Region selection and click calibration work in physical pixels
/// because capture and <c>SendInput</c> do; the app declares Per-Monitor-V2 so they line up.
/// </summary>
public static class DpiUtil
{
    /// <summary>Gets the DPI scale (1.0 = 96 DPI, 1.75 = 168 DPI) of the monitor under a physical point.</summary>
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

    /// <summary>Gets the virtual screen bounds (spanning all monitors) in physical pixels.</summary>
    public static RectInt32 GetVirtualScreenPhysical() => new(
        NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN),
        NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN),
        NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN),
        NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN));
}
