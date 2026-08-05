using System.Runtime.InteropServices;
using Windows.Graphics;

namespace FluxRead.Interop;

/// <summary>Fullscreen capture-region selector, in physical pixels. A layered Win32 window rather than
/// XAML, because a WinUI window's backdrop is opaque and 1.7 offers no transparent one.</summary>
public sealed class RegionSelectOverlay
{
    /// <summary>Shows the overlay and blocks until the user drags a rectangle or presses Escape.</summary>
    /// <returns>The chosen rectangle in physical screen pixels, or null if cancelled.</returns>
    public static RectInt32? Select()
    {
        var instance = new RegionSelectOverlay();
        return instance.Run();
    }

    /// <summary>Runs the overlay on its own STA thread — it pumps its own message loop, which would
    /// otherwise block the WinUI dispatcher.</summary>
    public static Task<RectInt32?> SelectAsync()
    {
        var completion = new TaskCompletionSource<RectInt32?>();
        var thread = new Thread(() =>
        {
            try
            {
                completion.SetResult(Select());
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        })
        {
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private const int WsExLayered = 0x00080000;
    private const int WsExTopmost = 0x00000008;
    private const int WsExToolWindow = 0x00000080;
    private const int WsPopup = unchecked((int)0x80000000);
    private const int SwShow = 5;
    private const uint LwaAlpha = 0x2;
    private const uint LwaColorKey = 0x1;
    private const int KeyColor = 0xFF00FF;   // BGR magenta; key-painted pixels become fully clear

    private const int WmDestroy = 0x0002;
    private const int WmPaint = 0x000F;
    private const int WmEraseBkgnd = 0x0014;
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
    private RectInt32? _result;

    // The class outlives every run, so its proc must too: a per-run delegate is collected once its
    // run ends, and the still-registered class then calls a freed thunk — the second open crashed.
    private static readonly WndProc SharedProc = (hwnd, msg, wParam, lParam) =>
        _current is { } overlay ? overlay.WindowProc(hwnd, msg, wParam, lParam)
            : DefWindowProc(hwnd, msg, wParam, lParam);
    private static readonly string ClassName = "FluxRegionOverlay" + Environment.ProcessId;
    private static RegionSelectOverlay? _current;
    private static bool _classRegistered;

    private RectInt32? Run()
    {
        _originX = GetSystemMetrics(SmXVirtualScreen);
        _originY = GetSystemMetrics(SmYVirtualScreen);
        int width = GetSystemMetrics(SmCxVirtualScreen);
        int height = GetSystemMetrics(SmCyVirtualScreen);

        if (!_classRegistered)
        {
            var wc = new WNDCLASSEX
            {
                cbSize = Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(SharedProc),
                hInstance = GetModuleHandle(null),
                lpszClassName = ClassName,
                hCursor = LoadCursor(IntPtr.Zero, 32515),   // IDC_CROSS
            };
            RegisterClassEx(ref wc);
            _classRegistered = true;
        }

        _current = this;
        _hwnd = CreateWindowEx(
            WsExLayered | WsExTopmost | WsExToolWindow, ClassName, "Flux region",
            WsPopup, _originX, _originY, width, height,
            IntPtr.Zero, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);

        // Uniform alpha dims the desktop; the dragged rectangle is painted in the key colour, so it
        // reads as a clear hole in the dim.
        SetLayeredWindowAttributes(_hwnd, KeyColor, 110, LwaAlpha | LwaColorKey);
        ShowWindow(_hwnd, SwShow);
        SetForegroundWindow(_hwnd);

        while (!_done && GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        if (_hwnd != IntPtr.Zero)
            DestroyWindow(_hwnd);

        _current = null;
        return _result;
    }

    private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WmLButtonDown:
                // The hole is transparent to hit-testing too, so the drag must be captured or the
                // pointer's messages fall through to the windows underneath it.
                SetCapture(hwnd);
                _dragging = true;
                _startX = _curX = LoWord(lParam);
                _startY = _curY = HiWord(lParam);
                return IntPtr.Zero;

            case WmMouseMove:
                if (_dragging)
                {
                    _curX = LoWord(lParam);
                    _curY = HiWord(lParam);
                    InvalidateRect(hwnd, IntPtr.Zero, false);
                }
                return IntPtr.Zero;

            // Every pixel is repainted from the buffer, so the erase pass would only flicker.
            case WmEraseBkgnd:
                return (IntPtr)1;

            case WmLButtonUp:
                if (_dragging)
                {
                    ReleaseCapture();
                    _dragging = false;
                    int x = Math.Min(_startX, _curX), y = Math.Min(_startY, _curY);
                    int w = Math.Abs(_curX - _startX), h = Math.Abs(_curY - _startY);
                    if (w > 4 && h > 4)
                        _result = new RectInt32(_originX + x, _originY + y, w, h);
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

    // Drawn into a memory bitmap and blitted once: painting dim-then-hole straight to the screen
    // shows the half-painted states as streaks while the marquee is dragged.
    private void Paint(IntPtr hwnd)
    {
        var dc = BeginPaint(hwnd, out var ps);
        GetClientRect(hwnd, out var rc);
        var mem = CreateCompatibleDC(dc);
        var surface = CreateCompatibleBitmap(dc, rc.Right, rc.Bottom);
        var oldSurface = SelectObject(mem, surface);

        var dim = CreateSolidBrush(0x14100C);   // BGR: the app's dark background
        FillRect(mem, ref rc, dim);
        DeleteObject(dim);

        if (_dragging || _result is not null)
        {
            int x = Math.Min(_startX, _curX), y = Math.Min(_startY, _curY);
            int w = Math.Abs(_curX - _startX), h = Math.Abs(_curY - _startY);
            var pen = CreatePen(0, 2, 0xFF5C7C);   // BGR of the accent violet
            var old = SelectObject(mem, pen);
            var key = CreateSolidBrush(KeyColor);  // clears the selection out of the dim
            var oldBrush = SelectObject(mem, key);
            Rectangle(mem, x, y, x + w, y + h);
            SelectObject(mem, old);
            SelectObject(mem, oldBrush);
            DeleteObject(pen);
            DeleteObject(key);
        }

        BitBlt(dc, 0, 0, rc.Right, rc.Bottom, mem, 0, 0, 0x00CC0020);   // SRCCOPY
        SelectObject(mem, oldSurface);
        DeleteObject(surface);
        DeleteDC(mem);
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
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr DefWindowProc(IntPtr h, int m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern int GetMessage(out MSG m, IntPtr h, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG m);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref MSG m);
    [DllImport("user32.dll")] private static extern void PostQuitMessage(int code);
    [DllImport("user32.dll")] private static extern bool InvalidateRect(IntPtr h, IntPtr r, bool erase);
    [DllImport("user32.dll")] private static extern IntPtr SetCapture(IntPtr h);
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(
        IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
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
