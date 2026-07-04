using System.Threading;

namespace FluxRead.Interop;

/// <summary>
/// Synthesizes a left-click at an absolute physical screen point via <c>SendInput</c> with
/// virtual-desktop-normalized coordinates (so it works across all monitors). Saves and restores
/// the cursor position so the loop does not leave the pointer sitting over the captured region.
/// </summary>
public static class MouseClicker
{
    private const int DownUpGapMs = 30;

    /// <summary>
    /// Clicks the left mouse button at the given physical screen point.
    /// </summary>
    /// <param name="physicalX">Physical X coordinate.</param>
    /// <param name="physicalY">Physical Y coordinate.</param>
    /// <param name="restoreCursor">Whether to move the cursor back afterward.</param>
    public static void ClickAt(int physicalX, int physicalY, bool restoreCursor = true)
    {
        NativeMethods.GetCursorPos(out var saved);

        int vx = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
        int vy = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
        int vw = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
        int vh = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);

        int nx = (int)Math.Round((physicalX - vx) * 65535.0 / Math.Max(1, vw - 1));
        int ny = (int)Math.Round((physicalY - vy) * 65535.0 / Math.Max(1, vh - 1));

        const uint absolute = NativeMethods.MOUSEEVENTF_ABSOLUTE | NativeMethods.MOUSEEVENTF_VIRTUALDESK;

        Send(nx, ny, absolute | NativeMethods.MOUSEEVENTF_MOVE | NativeMethods.MOUSEEVENTF_LEFTDOWN);
        Thread.Sleep(DownUpGapMs);
        Send(nx, ny, absolute | NativeMethods.MOUSEEVENTF_LEFTUP);

        if (restoreCursor)
        {
            Thread.Sleep(DownUpGapMs);
            NativeMethods.SetCursorPos(saved.X, saved.Y);
        }
    }

    private static void Send(int nx, int ny, uint flags)
    {
        var inputs = new[]
        {
            new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_MOUSE,
                mi = new NativeMethods.MOUSEINPUT { dx = nx, dy = ny, dwFlags = flags },
            },
        };

        NativeMethods.SendInput((uint)inputs.Length, inputs, System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.INPUT>());
    }
}
