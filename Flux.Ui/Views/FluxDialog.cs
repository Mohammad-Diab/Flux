using System.Runtime.InteropServices;
using Flux.Ui.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Flux.Ui.Views;

/// <summary>Base for the app's dialogs: card radius, ContentDialog's own open/close transition
/// behind the motion gate, and drag-to-move — a modal dialog's smoke layer swallows the hosting
/// window's caption region, so dragging any non-interactive part of the dialog moves the window
/// (buttons keep their presses; they never bubble here).</summary>
public class FluxDialog : ContentDialog
{
    private const int WmNcLButtonDown = 0x00A1;
    private const nint HtCaption = 2;

    protected FluxDialog()
    {
        CornerRadius = new CornerRadius(14);
        PointerPressed += OnDragPointerPressed;
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (MotionSettings.Current.AnimationsEnabled || GetTemplateChild("Container") is not FrameworkElement container)
            return;

        // The scale-and-fade is a VisualTransition in the stock template, so gating it means removing it.
        foreach (var group in VisualStateManager.GetVisualStateGroups(container))
            group.Transitions.Clear();
    }

    private void OnDragPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        if (XamlRoot?.ContentIslandEnvironment is not { } island)
            return;

        nint hwnd = Microsoft.UI.Win32Interop.GetWindowFromWindowId(island.AppWindowId);
        if (hwnd == 0)
            return;

        // The native move loop tracks the mouse from here on, exactly as a title-bar drag would.
        ReleaseCapture();
        SendMessage(hwnd, WmNcLButtonDown, HtCaption, 0);
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint hWnd, int msg, nint wParam, nint lParam);
}
