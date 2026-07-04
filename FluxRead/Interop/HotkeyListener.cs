using System.Windows;
using System.Windows.Interop;

namespace FluxRead.Interop;

/// <summary>
/// Registers a global F8 hotkey against a window and raises <see cref="Pressed"/> when it fires.
/// Used for Next-button calibration: the user hovers over the button and presses F8, avoiding a
/// low-level mouse hook and any risk of swallowing the real click.
/// </summary>
public sealed class HotkeyListener : IDisposable
{
    private const int HotkeyId = 0xF10C;

    private readonly HwndSource _source;
    private bool _armed;
    private bool _disposed;

    /// <summary>Raised on the UI thread when the hotkey is pressed while armed.</summary>
    public event EventHandler? Pressed;

    /// <summary>
    /// Initializes a new instance of the <see cref="HotkeyListener"/> class bound to a window.
    /// </summary>
    /// <param name="window">The window that owns the hotkey registration.</param>
    public HotkeyListener(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var handle = new WindowInteropHelper(window).EnsureHandle();
        _source = HwndSource.FromHwnd(handle)
            ?? throw new InvalidOperationException("Window has no HWND source.");
        _source.AddHook(WndProc);
    }

    /// <summary>Registers the F8 hotkey. Idempotent.</summary>
    public void Arm()
    {
        if (_armed || _disposed)
            return;

        _armed = NativeMethods.RegisterHotKey(_source.Handle, HotkeyId, NativeMethods.MOD_NONE, NativeMethods.VK_F8);
        if (!_armed)
            throw new InvalidOperationException("Failed to register the F8 hotkey (already held by another app?).");
    }

    /// <summary>Unregisters the hotkey. Idempotent.</summary>
    public void Disarm()
    {
        if (!_armed)
            return;

        NativeMethods.UnregisterHotKey(_source.Handle, HotkeyId);
        _armed = false;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            Pressed?.Invoke(this, EventArgs.Empty);
            handled = true;
        }

        return IntPtr.Zero;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;

        Disarm();
        _source.RemoveHook(WndProc);
        _disposed = true;
    }
}
