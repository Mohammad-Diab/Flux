using System.Runtime.InteropServices;

namespace FluxRead.WinUI.Interop;

/// <summary>Fullscreen capture-region selector, in physical pixels. A layered Win32 window rather than
/// XAML, because a WinUI window's backdrop is opaque and 1.7 offers no transparent one.</summary>
public sealed class RegionSelectOverlay
{
    /// <summary>Shows the overlay and blocks until the user drags a rectangle or presses Escape.</summary>
    /// <returns>The chosen rectangle in physical screen pixels, or null if cancelled.</returns>
    public static (int X, int Y, int Width, int Height)? Select()
    {
        var instance = new RegionSelectOverlay();
        return instance.Run();
    }

    private const int WsExLayered = 0x00080000;
    private const int WsExTopmost = 0x00000008;
    private const int WsExToolWindow = 0x00000080;
    private const int WsPopup = unchecked((int)0x80000000);
    private const int SwShow = 5;
    private const uint LwaAlpha = 0x2;

    private const int WmDestroy = 0x0002;
    private const int WmPaint = 0x000F;
    private const int WmKeyDown = 0x0100;
    private const int WmLButtonDown = 0x0201;
    private const int WmLButtonUp = 0x0202;
    private const int WmMouseMove = 0x0200;
    private const int VkEscape = 0x1B;

    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;

    private IntPtr _hwnd;
    private int _originX, _originY;
    private int _startX, _startY, _curX, _curY;
    private bool _dragging, _done;
    private (int X, int Y, int Width, int Height)? _result;

    private (int X, int Y, int Width, int Height)? Run()
    {
        _originX = GetSystemMetrics(SmXVirtualScreen);
        _originY = GetSystemMetrics(SmYVirtualScreen);
        int width = GetSystemMetrics(SmCxVirtualScreen);
        int height = GetSystemMetrics(SmCyVirtualScreen);

        var proc = new WndProc(WindowProc);
        var wc = new WNDCLASSEX
        {
            cbSize = Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(proc),
            hInstance = GetModuleHandle(null),
            lpszClassName = "FluxRegionOverlay" + Environment.ProcessId,
            hCursor = LoadCursor(IntPtr.Zero, 32515),   // IDC_CROSS
        };
        RegisterClassEx(ref wc);

        _hwnd = CreateWindowEx(
            WsExLayered | WsExTopmost | WsExToolWindow, wc.lpszClassName, "Flux region",
            WsPopup, _originX, _originY, width, height,
            IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);

        // Uniform alpha over the whole window, so the frame being selected stays visible underneath.
        SetLayeredWindowAttributes(_hwnd, 0, 110, LwaAlpha);
        ShowWindow(_hwnd, SwShow);
        SetForegroundWindow(_hwnd);

        while (!_done && GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        if (_hwnd != IntPtr.Zero)
            DestroyWindow(_hwnd);

        GC.KeepAlive(proc);
        return _result;
    }

    private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WmLButtonDown:
                _dragging = true;
                _startX = _curX = LoWord(lParam);
                _startY = _curY = HiWord(lParam);
                return IntPtr.Zero;

            case WmMouseMove:
                if (_dragging)
                {
                    _curX = LoWord(lParam);
                    _curY = HiWord(lParam);
                    InvalidateRect(hwnd, IntPtr.Zero, true);
                }
                return IntPtr.Zero;

            case WmLButtonUp:
                if (_dragging)
                {
                    _dragging = false;
                    int x = Math.Min(_startX, _curX), y = Math.Min(_startY, _curY);
                    int w = Math.Abs(_curX - _startX), h = Math.Abs(_curY - _startY);
                    if (w > 4 && h > 4)
                        _result = (_originX + x, _originY + y, w, h);
                    _done = true;
                    PostQuitMessage(0);
                }
                return IntPtr.Zero;

            case WmKeyDown when (int)wParam == VkEscape:
                _result = null;
                _done = true;
                PostQuitMessage(0);
                return IntPtr.Zero;

            case WmPaint:
                Paint(hwnd);
                return IntPtr.Zero;

            case WmDestroy:
                _done = true;
                PostQuitMessage(0);
                return IntPtr.Zero;
        }

        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private void Paint(IntPtr hwnd)
    {
        var dc = BeginPaint(hwnd, out var ps);
        var dim = CreateSolidBrush(0x14100C);   // BGR: the app's dark background
        FillRect(dc, ref ps.rcPaint, dim);
        DeleteObject(dim);

        if (_dragging || _result is not null)
        {
            int x = Math.Min(_startX, _curX), y = Math.Min(_startY, _curY);
            int w = Math.Abs(_curX - _startX), h = Math.Abs(_curY - _startY);
            var pen = CreatePen(0, 2, 0xFF5C7C);   // BGR of the accent violet
            var old = SelectObject(dc, pen);
            var hollow = GetStockObject(5);        // NULL_BRUSH
            var oldBrush = SelectObject(dc, hollow);
            Rectangle(dc, x, y, x + w, y + h);
            SelectObject(dc, old);
            SelectObject(dc, oldBrush);
            DeleteObject(pen);
        }

        EndPaint(hwnd, ref ps);
    }

    private static int LoWord(IntPtr v) => unchecked((short)(long)v);
    private static int HiWord(IntPtr v) => unchecked((short)((long)v >> 16));

    private delegate IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public int cbSize, style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PAINTSTRUCT
    {
        public IntPtr hdc;
        public bool fErase;
        public RECT rcPaint;
        public bool fRestore, fIncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] rgbReserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public int message;
        public IntPtr wParam, lParam;
        public int time, pt_x, pt_y;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClassEx(ref WNDCLASSEX c);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateWindowEx(
        int exStyle, string cls, string name, int style, int x, int y, int w, int h,
        IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(IntPtr h, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int i);
    [DllImport("user32.dll")] private static extern IntPtr DefWindowProc(IntPtr h, int m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern int GetMessage(out MSG m, IntPtr h, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG m);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref MSG m);
    [DllImport("user32.dll")] private static extern void PostQuitMessage(int code);
    [DllImport("user32.dll")] private static extern bool InvalidateRect(IntPtr h, IntPtr r, bool erase);
    [DllImport("user32.dll")] private static extern IntPtr BeginPaint(IntPtr h, out PAINTSTRUCT ps);
    [DllImport("user32.dll")] private static extern bool EndPaint(IntPtr h, ref PAINTSTRUCT ps);
    [DllImport("user32.dll")] private static extern int FillRect(IntPtr dc, ref RECT r, IntPtr brush);
    [DllImport("user32.dll")] private static extern IntPtr LoadCursor(IntPtr inst, int id);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateSolidBrush(int color);
    [DllImport("gdi32.dll")] private static extern IntPtr CreatePen(int style, int width, int color);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern IntPtr GetStockObject(int i);
    [DllImport("gdi32.dll")] private static extern bool Rectangle(IntPtr dc, int l, int t, int r, int b);
}
