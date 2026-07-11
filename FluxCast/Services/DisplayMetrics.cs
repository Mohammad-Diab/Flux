using System.Runtime.InteropServices;

namespace FluxCast.Services;

/// <summary>
/// Physical-pixel size of the primary screen. FluxCast is Per-Monitor-V2 DPI-aware, so
/// <c>GetSystemMetrics</c> returns device pixels — the budget the frame grid is fitted to.
/// </summary>
public static class DisplayMetrics
{
    /// <summary>Gets the primary screen's width and height in physical pixels.</summary>
    public static (int Width, int Height) PrimaryScreenPixels()
    {
        int width = GetSystemMetrics(SM_CXSCREEN);
        int height = GetSystemMetrics(SM_CYSCREEN);
        return (width > 0 ? width : 1920, height > 0 ? height : 1080);
    }

    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);
}
