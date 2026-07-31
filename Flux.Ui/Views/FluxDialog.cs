using Flux.Ui.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Flux.Ui.Views;

/// <summary>Base for the app's dialogs: card radius, and ContentDialog's own open/close transition
/// behind the motion gate.</summary>
public class FluxDialog : ContentDialog
{
    protected FluxDialog() => CornerRadius = new CornerRadius(14);

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (MotionSettings.Current.AnimationsEnabled || GetTemplateChild("Container") is not FrameworkElement container)
            return;

        // The scale-and-fade is a VisualTransition in the stock template, so gating it means removing it.
        foreach (var group in VisualStateManager.GetVisualStateGroups(container))
            group.Transitions.Clear();
    }
}
