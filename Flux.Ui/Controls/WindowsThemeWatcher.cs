using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Flux.Ui.Services;

namespace Flux.Ui.Controls;

/// <summary>
/// Re-applies the theme when Windows broadcasts an app-theme change, but only while the user's
/// preference is "follow System". Hooks the main window's message loop for WM_SETTINGCHANGE.
/// </summary>
public sealed class WindowsThemeWatcher
{
    private const int WmSettingChange = 0x001A;

    private readonly ThemeService _theme;
    private readonly Func<AppThemeMode> _currentMode;

    public WindowsThemeWatcher(ThemeService theme, Func<AppThemeMode> currentMode)
    {
        _theme = theme;
        _currentMode = currentMode;
    }

    /// <summary>Begins listening for OS theme changes via <paramref name="window"/>'s handle.</summary>
    public void Attach(Window window)
    {
        window.SourceInitialized += (_, _) =>
        {
            if (PresentationSource.FromVisual(window) is HwndSource source)
                source.AddHook(Hook);
        };
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmSettingChange && lParam != IntPtr.Zero &&
            Marshal.PtrToStringUni(lParam) == "ImmersiveColorSet" &&
            _currentMode() == AppThemeMode.System)
        {
            _theme.Apply(AppThemeMode.System);
        }

        return IntPtr.Zero;
    }
}
