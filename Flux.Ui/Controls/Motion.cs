using System.ComponentModel;
using Microsoft.UI.Xaml;

namespace Flux.Ui.Controls;

/// <summary>
/// Puts a control template's state changes behind <see cref="MotionSettings"/>. Set
/// <c>controls:Motion.GateTransitions="True"</c> on the element owning the VisualStateGroups: its
/// transitions are removed while motion is off, so the states snap, and restored when it comes back.
/// WinUI has no MultiTrigger to branch a declarative animation on, which is how the WPF styles did it.
/// </summary>
public static class Motion
{
    public static readonly DependencyProperty GateTransitionsProperty = DependencyProperty.RegisterAttached(
        "GateTransitions", typeof(bool), typeof(Motion), new PropertyMetadata(false, OnGateChanged));

    public static void SetGateTransitions(FrameworkElement element, bool value) =>
        element.SetValue(GateTransitionsProperty, value);

    public static bool GetGateTransitions(FrameworkElement element) =>
        (bool)element.GetValue(GateTransitionsProperty);

    private static void OnGateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element || !(bool)e.NewValue)
            return;

        // Each group's transitions are captured the first time they are seen, so Apply can run before
        // the groups exist (template children load after the templated parent) and again afterwards.
        var saved = new Dictionary<VisualStateGroup, List<VisualTransition>>();

        void Apply()
        {
            bool animate = MotionSettings.Current.AnimationsEnabled;
            foreach (var group in VisualStateManager.GetVisualStateGroups(element))
            {
                if (!saved.TryGetValue(group, out var original))
                    saved[group] = original = [.. group.Transitions];

                group.Transitions.Clear();
                if (!animate)
                    continue;
                foreach (var transition in original)
                    group.Transitions.Add(transition);
            }
        }

        PropertyChangedEventHandler onMotionChanged = (_, _) => Apply();
        MotionSettings.Current.PropertyChanged += onMotionChanged;
        element.Unloaded += (_, _) => MotionSettings.Current.PropertyChanged -= onMotionChanged;
        element.Loaded += (_, _) => Apply();
        Apply();
    }
}
