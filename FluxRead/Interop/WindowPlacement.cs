using System.Runtime.InteropServices;
using FluxRead.Interop;
using Windows.Graphics;

namespace FluxRead.Interop;

/// <summary>
/// Keeps the app's own windows out of the captured screen region, so they never pollute a capture.
/// Geometric relocation is the primary defense; capture exclusion is optional hardening.
/// </summary>
public static class WindowPlacement
{
    /// <summary>
    /// If the window overlaps the capture region, moves it (without resizing) to the widest free
    /// band of the virtual screen beside the region, preferring the larger side.
    /// </summary>
    /// <param name="hwnd">Window handle.</param>
    /// <param name="regionPhysical">Capture region in physical pixels.</param>
    /// <returns>True if the window was moved.</returns>
    public static bool EnsureOutsideRegion(IntPtr hwnd, RectInt32 regionPhysical)
    {
        if (!NativeMethods.GetWindowRect(hwnd, out var win))
            return false;

        if (!Intersects(win, regionPhysical))
            return false;

        var screen = DpiUtil.GetVirtualScreenPhysical();
        int width = win.Width;
        int height = win.Height;

        int regionRight = regionPhysical.X + regionPhysical.Width;
        int spaceLeft = regionPhysical.X - screen.X;
        int spaceRight = screen.X + screen.Width - regionRight;

        int targetX;
        int targetY = Math.Clamp(win.Top, screen.Y, screen.Y + Math.Max(0, screen.Height - height));

        if (spaceRight >= width && spaceRight >= spaceLeft)
        {
            targetX = regionRight;
        }
        else if (spaceLeft >= width)
        {
            targetX = regionPhysical.X - width;
        }
        else
        {
            // No horizontal room: drop below the region if possible, else pin to the top.
            int regionBottom = regionPhysical.Y + regionPhysical.Height;
            targetX = Math.Clamp(win.Left, screen.X, screen.X + Math.Max(0, screen.Width - width));
            targetY = regionBottom + height <= screen.Y + screen.Height
                ? regionBottom
                : Math.Max(screen.Y, regionPhysical.Y - height);
        }

        NativeMethods.SetWindowPos(
            hwnd, IntPtr.Zero, targetX, targetY, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
        return true;
    }

    /// <summary>Parks a window bottom-right of <paramref name="referenceWindow"/>'s monitor work area
    /// (or <paramref name="hwnd"/>'s if none), sized for that monitor's DPI. Physical pixels, so it
    /// lands correctly across mixed-DPI monitors; the size arguments are DIPs.</summary>
    public static void PlaceBottomRightOfMonitor(
        IntPtr hwnd, IntPtr referenceWindow, double widthDip, double heightDip, double marginDip)
    {
        var reference = referenceWindow != IntPtr.Zero ? referenceWindow : hwnd;
        var monitor = NativeMethods.MonitorFromWindow(reference, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var info = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info))
            return;

        double scale = NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0
            ? dpiX / 96.0
            : 1.0;

        int w = (int)Math.Round(widthDip * scale);
        int h = (int)Math.Round(heightDip * scale);
        int m = (int)Math.Round(marginDip * scale);
        int x = info.rcWork.Right - w - m;
        int y = info.rcWork.Bottom - h - m;

        NativeMethods.SetWindowPos(
            hwnd, IntPtr.Zero, x, y, w, h, NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
    }

    /// <summary>
    /// Sets or clears capture exclusion (Win10 2004+). Excluded windows render as black in GDI
    /// captures, so this complements — not replaces — geometric relocation.
    /// </summary>
    /// <param name="hwnd">Window handle.</param>
    /// <param name="exclude">Whether to exclude the window from screen capture.</param>
    public static void SetExcludeFromCapture(IntPtr hwnd, bool exclude) =>
        NativeMethods.SetWindowDisplayAffinity(
            hwnd, exclude ? NativeMethods.WDA_EXCLUDEFROMCAPTURE : NativeMethods.WDA_NONE);

    private static bool Intersects(NativeMethods.RECT window, RectInt32 region) =>
        window.Left < region.X + region.Width &&
        window.Right > region.X &&
        window.Top < region.Y + region.Height &&
        window.Bottom > region.Y;
}
