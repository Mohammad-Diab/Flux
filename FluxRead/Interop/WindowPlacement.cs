using System.Windows;

namespace FluxRead.Interop;

/// <summary>
/// Keeps the Server's own window out of the captured screen region, so it never pollutes a
/// capture. Geometric relocation is the primary defense; capture exclusion is optional hardening.
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
    public static bool EnsureOutsideRegion(IntPtr hwnd, Int32Rect regionPhysical)
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

    /// <summary>
    /// Sets or clears capture exclusion (Win10 2004+). Excluded windows render as black in GDI
    /// captures, so this complements — not replaces — geometric relocation.
    /// </summary>
    /// <param name="hwnd">Window handle.</param>
    /// <param name="exclude">Whether to exclude the window from screen capture.</param>
    public static void SetExcludeFromCapture(IntPtr hwnd, bool exclude) =>
        NativeMethods.SetWindowDisplayAffinity(
            hwnd, exclude ? NativeMethods.WDA_EXCLUDEFROMCAPTURE : NativeMethods.WDA_NONE);

    private static bool Intersects(NativeMethods.RECT window, Int32Rect region) =>
        window.Left < region.X + region.Width &&
        window.Right > region.X &&
        window.Top < region.Y + region.Height &&
        window.Bottom > region.Y;
}
