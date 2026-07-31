using System.Runtime.InteropServices;

namespace Flux.Ui.Interop;

/// <summary>Drives the taskbar button's progress bar. WinUI has no <c>TaskbarItemInfo</c> to bind, so
/// this calls <c>ITaskbarList3</c> directly against the handle the shell hands it.</summary>
public sealed class TaskbarProgress
{
    private const uint NoProgress = 0x0;
    private const uint Indeterminate = 0x1;
    private const uint Normal = 0x2;

    private static readonly Lazy<TaskbarProgress> Instance = new(() => new TaskbarProgress());

    private ITaskbarList3? _taskbar;
    private IntPtr _hwnd;

    /// <summary>The shared instance; view models push progress into it.</summary>
    public static TaskbarProgress Current => Instance.Value;

    /// <summary>Binds the progress to a window. Failing to create the shell object is not fatal.</summary>
    public void Attach(IntPtr hwnd)
    {
        _hwnd = hwnd;
        try
        {
            _taskbar = (ITaskbarList3)new TaskbarList();
            _taskbar.HrInit();
        }
        catch (COMException)
        {
            _taskbar = null;
        }
    }

    /// <summary>Shows a determinate bar at <paramref name="fraction"/> (0-1).</summary>
    public void Report(double fraction)
    {
        if (_taskbar is null)
            return;

        _taskbar.SetProgressState(_hwnd, Normal);
        _taskbar.SetProgressValue(_hwnd, (ulong)(Math.Clamp(fraction, 0, 1) * 1000), 1000);
    }

    /// <summary>Shows an indeterminate (marching) bar.</summary>
    public void SetIndeterminate() => _taskbar?.SetProgressState(_hwnd, Indeterminate);

    /// <summary>Hides the taskbar progress.</summary>
    public void Clear() => _taskbar?.SetProgressState(_hwnd, NoProgress);

    [ComImport]
    [Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
    [ClassInterface(ClassInterfaceType.None)]
    private class TaskbarList
    {
    }

    [ComImport]
    [Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        // ITaskbarList, then ITaskbarList2: the vtable order has to be declared to reach ITaskbarList3.
        void HrInit();
        void AddTab(IntPtr hwnd);
        void DeleteTab(IntPtr hwnd);
        void ActivateTab(IntPtr hwnd);
        void SetActiveAlt(IntPtr hwnd);
        void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
        void SetProgressValue(IntPtr hwnd, ulong completed, ulong total);
        void SetProgressState(IntPtr hwnd, uint flags);
    }
}
