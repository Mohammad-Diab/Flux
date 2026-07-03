using System.Runtime.InteropServices;

namespace FluxRead.Services;

/// <summary>
/// Service for positioning and resizing windows (Windows-specific).
/// </summary>
public class WindowPositioningService
{
#if WINDOWS
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, 
        int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
      public int Right;
  public int Bottom;
    }

 private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;
    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
 private const uint SWP_SHOWWINDOW = 0x0040;
 private const uint SWP_NOACTIVATE = 0x0010;
#endif

  /// <summary>
    /// Moves the current window to compact capture mode (top-right corner).
    /// </summary>
    public void MoveToCompactMode(int width = 300, int height = 100)
    {
#if WINDOWS
        try
        {
   var hwnd = GetForegroundWindow();
      if (hwnd == IntPtr.Zero)
        return;

 var screenWidth = GetSystemMetrics(SM_CXSCREEN);
         var screenHeight = GetSystemMetrics(SM_CYSCREEN);

          // Position in top-right corner with margin
      var x = screenWidth - width - 20;
     var y = 20;

       // Set window position (topmost, no activation)
       SetWindowPos(hwnd, HWND_TOPMOST, x, y, width, height, 
   SWP_SHOWWINDOW | SWP_NOACTIVATE);
   }
  catch
      {
     // Fallback: do nothing if positioning fails
        }
#endif
    }

    /// <summary>
    /// Restores the window to normal mode.
    /// </summary>
    public void RestoreToNormalMode(int width = 1000, int height = 700)
    {
#if WINDOWS
   try
        {
      var hwnd = GetForegroundWindow();
   if (hwnd == IntPtr.Zero)
       return;

   var screenWidth = GetSystemMetrics(SM_CXSCREEN);
            var screenHeight = GetSystemMetrics(SM_CYSCREEN);

      // Center the window
  var x = (screenWidth - width) / 2;
   var y = (screenHeight - height) / 2;

            // Set window position (normal layer, centered)
  SetWindowPos(hwnd, IntPtr.Zero, x, y, width, height, SWP_SHOWWINDOW);
 }
     catch
 {
      // Fallback: do nothing if positioning fails
  }
#endif
    }

    /// <summary>
    /// Gets the current window handle.
  /// </summary>
 public IntPtr GetCurrentWindowHandle()
    {
#if WINDOWS
  return GetForegroundWindow();
#else
        return IntPtr.Zero;
#endif
    }
}
