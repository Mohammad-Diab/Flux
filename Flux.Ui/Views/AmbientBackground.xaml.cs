using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using Flux.Ui.Controls;

namespace Flux.Ui.Views;

/// <summary>
/// Non-interactive layer of slow-drifting spectrum glow orbs behind the app content. It is hidden
/// entirely (its gradients not rendered, its drift stopped) when animations and effects are
/// disabled for performance, and updates live when the setting changes.
/// </summary>
public partial class AmbientBackground : UserControl
{
    private Storyboard? _drift;

    public AmbientBackground()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _drift ??= (Storyboard)Resources["Drift"];
        MotionSettings.Current.PropertyChanged += OnMotionChanged;
        ApplyMotionSetting();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        MotionSettings.Current.PropertyChanged -= OnMotionChanged;
        _drift?.Stop(this);
    }

    private void OnMotionChanged(object? sender, PropertyChangedEventArgs e) => ApplyMotionSetting();

    private void ApplyMotionSetting()
    {
        bool enabled = MotionSettings.Current.AnimationsEnabled;
        Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;

        if (_drift is null)
            return;
        if (enabled)
            _drift.Begin(this, isControllable: true);
        else
            _drift.Stop(this);
    }
}
