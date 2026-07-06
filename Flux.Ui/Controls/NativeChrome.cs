using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Flux.Ui.Controls;

/// <summary>
/// Re-enables native Windows minimize/maximize/restore animations (plus the system menu and
/// taskbar behavior) for borderless WindowChrome windows. <c>WindowStyle="None"</c> strips the
/// WS_MINIMIZEBOX / WS_MAXIMIZEBOX / WS_SYSMENU bits that Windows uses to decide whether to play
/// those animations, so we add them back on the HWND while WindowChrome keeps the caption hidden.
/// </summary>
public static class NativeChrome
{
    private const int GwlStyle = -16;
    private const int WsMaximizeBox = 0x00010000;
    private const int WsMinimizeBox = 0x00020000;
    private const int WsSysMenu = 0x00080000;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr hWnd, int index, int newLong);

    /// <summary>Safe to call once the native handle exists.</summary>
    public static void EnableWindowAnimations(Window window)
    {
        if (!MotionSettings.Current.AnimationsEnabled)
            return;

        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        int style = GetWindowLong(hwnd, GwlStyle);
        SetWindowLong(hwnd, GwlStyle, style | WsMinimizeBox | WsMaximizeBox | WsSysMenu);
    }
}
