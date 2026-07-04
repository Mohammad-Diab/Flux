using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FluxCast.Controls;

/// <summary>
/// Opts a borderless window into the Windows 11 rounded-corner treatment via DWM. On Windows 10
/// and earlier the attribute is unknown and the call is silently ignored, so those systems keep
/// their default (square) corners — no version checks required.
/// </summary>
public static class Win11Corners
{
    // DWMWA_WINDOW_CORNER_PREFERENCE (Windows 11 22000+). Value 2 == DWMWCP_ROUND.
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpRound = 2;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>
    /// Requests rounded corners for the given window. Safe to call on any Windows version.
    /// </summary>
    /// <param name="window">The window to round; its native handle must already exist.</param>
    public static void Apply(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        int preference = DwmwcpRound;
        try
        {
            DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref preference, sizeof(int));
        }
        catch (DllNotFoundException)
        {
            // Pre-DWM (unsupported here in practice); leave OS-default corners.
        }
        catch (EntryPointNotFoundException)
        {
            // Older dwmapi without this attribute; leave OS-default corners.
        }
    }
}
