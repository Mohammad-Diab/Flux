using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Flux.Ui.Controls;

/// <summary>
/// Drives one of WinUI's <c>AnimatedIcon</c>s from a button's pointer states, behind
/// <see cref="MotionSettings"/>. Setting <c>AnimatedIcon.State</c> from the template's VisualStates
/// instead plays the transition only once — the icon is per-instance content, so the state has to be
/// set on the icon itself. With motion off the state is left alone and the icon simply rests.
/// </summary>
public static class MotionIcon
{
    public static void Attach(Button button, AnimatedIcon icon)
    {
        AnimatedIcon.SetState(icon, "Normal");

        void Set(string state)
        {
            if (MotionSettings.Current.AnimationsEnabled)
                AnimatedIcon.SetState(icon, state);
        }

        // ButtonBase marks the pointer events it handles, so a plain += never runs for some of them.
        button.AddHandler(UIElement.PointerEnteredEvent,
            new PointerEventHandler((_, _) => Set("PointerOver")), handledEventsToo: true);
        button.AddHandler(UIElement.PointerExitedEvent,
            new PointerEventHandler((_, _) => Set("Normal")), handledEventsToo: true);
        button.AddHandler(UIElement.PointerCaptureLostEvent,
            new PointerEventHandler((_, _) => Set("Normal")), handledEventsToo: true);
        button.AddHandler(UIElement.PointerPressedEvent,
            new PointerEventHandler((_, _) => Set("Pressed")), handledEventsToo: true);
        button.AddHandler(UIElement.PointerReleasedEvent,
            new PointerEventHandler((_, _) => Set("PointerOver")), handledEventsToo: true);
    }
}
