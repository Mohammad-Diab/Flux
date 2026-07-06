using System.Windows;
using System.Windows.Data;
using System.Windows.Shell;

namespace Flux.Ui.Controls;

/// <summary>One-call chrome wiring for FluxWindow-styled windows.</summary>
public static class FluxWindowChrome
{
    /// <summary>Open/minimize animations, native transitions, rounded corners, shared taskbar progress.</summary>
    public static void Attach(Window window, FrameworkElement rootContent)
    {
        WindowChromeAnimator.Attach(window, rootContent);
        window.SourceInitialized += (_, _) =>
        {
            NativeChrome.EnableWindowAnimations(window);
            Win11Corners.Apply(window);
        };
        BindTaskbarProgress(window);
    }

    /// <summary>Corners + taskbar progress only, for tool windows without the animated chrome.</summary>
    public static void AttachCompact(Window window)
    {
        window.SourceInitialized += (_, _) => Win11Corners.Apply(window);
        BindTaskbarProgress(window);
    }

    private static void BindTaskbarProgress(Window window)
    {
        var info = new TaskbarItemInfo();
        BindingOperations.SetBinding(info, TaskbarItemInfo.ProgressStateProperty,
            new Binding(nameof(TaskbarProgress.State)) { Source = TaskbarProgress.Current });
        BindingOperations.SetBinding(info, TaskbarItemInfo.ProgressValueProperty,
            new Binding(nameof(TaskbarProgress.Value)) { Source = TaskbarProgress.Current });
        window.TaskbarItemInfo = info;
    }
}
