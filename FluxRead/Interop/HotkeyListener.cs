using System.Runtime.InteropServices;
using FluxRead.Interop;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace FluxRead.Interop;

/// <summary>
/// Registers a global F8 hotkey against a window and raises <see cref="Pressed"/> when it fires.
/// Used for Next-button calibration: the user hovers over the button and presses F8, avoiding a
/// low-level mouse hook and any risk of swallowing the real click.
/// </summary>
public sealed class HotkeyListener : IDisposable
{
    private const int HotkeyId = 0xF10C;
    private const int GwlpWndProc = -4;

    private readonly IntPtr _hwnd;
    private readonly WndProc _proc;
    private readonly IntPtr _previousProc;
    private bool _armed;
    private bool _disposed;

    /// <summary>Raised on the UI thread when the hotkey is pressed while armed.</summary>
    public event EventHandler? Pressed;

    /// <summary>WinUI has no <c>HwndSource.AddHook</c>, so WM_HOTKEY is picked up by chaining the
    /// window's own window procedure.</summary>
    public HotkeyListener(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        _hwnd = WindowNative.GetWindowHandle(window);
        _proc = WindowProc;
        _previousProc = SetWindowLongPtr(_hwnd, GwlpWndProc, Marshal.GetFunctionPointerForDelegate(_proc));
        if (_previousProc == IntPtr.Zero)
            throw new InvalidOperationException("Could not hook the window for the hotkey.");
    }

    /// <summary>Registers the F8 hotkey. Idempotent.</summary>
    public void Arm()
    {
        if (_armed || _disposed)
            return;

        _armed = NativeMethods.RegisterHotKey(_hwnd, HotkeyId, NativeMethods.MOD_NONE, NativeMethods.VK_F8);
        if (!_armed)
            throw new InvalidOperationException("Failed to register the F8 hotkey (already held by another app?).");
    }

    /// <summary>Unregisters the hotkey. Idempotent.</summary>
    public void Disarm()
    {
        if (!_armed)
            return;

        NativeMethods.UnregisterHotKey(_hwnd, HotkeyId);
        _armed = false;
    }

    private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            Pressed?.Invoke(this, EventArgs.Empty);
            return IntPtr.Zero;
        }

        return CallWindowProc(_previousProc, hwnd, msg, wParam, lParam);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;

        Disarm();
        SetWindowLongPtr(_hwnd, GwlpWndProc, _previousProc);
        _disposed = true;
        GC.KeepAlive(_proc);
    }

    private delegate IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
    private static extern IntPtr CallWindowProc(IntPtr previous, IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
}
