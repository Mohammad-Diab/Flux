using System.Windows;

namespace Flux.Ui.Controls;

/// <summary>
/// Motion.Enabled attached property — mirrors <see cref="MotionSettings"/> onto a control so
/// template MultiTriggers can branch between animated and instant states.
/// </summary>
public static class Motion
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(Motion), new PropertyMetadata(true));

    public static bool GetEnabled(DependencyObject element) => (bool)element.GetValue(EnabledProperty);

    public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);
}
