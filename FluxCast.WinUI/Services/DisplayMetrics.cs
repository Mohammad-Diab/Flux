using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace FluxCast.WinUI.Services;

/// <summary>
/// The usable presenter canvas in physical pixels: the work area of the monitor the window is on,
/// minus the presenter's own chrome. The frame grid is fitted to this (not the raw screen) so a
/// maximised presenter shows tiles at near-native size without tripping the small-tile warning.
/// FluxCast is Per-Monitor-V2 DPI-aware, so these are device pixels.
/// </summary>
public static class DisplayMetrics
{
    // PresenterView chrome around the frame (title bar + top toolbar + bottom strip + margins), in DIPs.
    // Slightly over-estimated so a maximised window clears the warning threshold with margin.
    private const int ChromeHeightDip = 190;
    private const int ChromeWidthDip = 24;

    /// <summary>Usable presenter canvas (physical px) on the monitor <paramref name="window"/> occupies.</summary>
    public static (int Width, int Height) PresenterCanvasPixels(Window? window)
    {
        var handle = window is null ? IntPtr.Zero : WindowNative.GetWindowHandle(window);
        if (handle == IntPtr.Zero)
            return PrimaryCanvasPixels();

        var monitor = MonitorFromWindow(handle, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info))
            return PrimaryCanvasPixels();

        double scale = GetDpiForWindow(handle) / 96.0;
        return Usable(info.rcWork.Right - info.rcWork.Left, info.rcWork.Bottom - info.rcWork.Top, scale);
    }

    private static (int Width, int Height) PrimaryCanvasPixels()
    {
        int w = GetSystemMetrics(SM_CXSCREEN), h = GetSystemMetrics(SM_CYSCREEN);
        return Usable(w > 0 ? w : 1920, h > 0 ? h : 1080, 1.0);
    }

    private static (int Width, int Height) Usable(int width, int height, double scale) =>
        (Math.Max(320, width - (int)(ChromeWidthDip * scale)),
         Math.Max(240, height - (int)(ChromeHeightDip * scale)));

    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);
}
