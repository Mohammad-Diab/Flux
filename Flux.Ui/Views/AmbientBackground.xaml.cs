using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using Flux.Ui.Controls;

namespace Flux.Ui.Views;

/// <summary>
/// Non-interactive layer of slow-drifting spectrum glow orbs behind the app content. The drift
/// runs only while motion is enabled, and starts/stops live when the setting changes.
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
        UpdateDrift();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        MotionSettings.Current.PropertyChanged -= OnMotionChanged;
        _drift?.Stop(this);
    }

    private void OnMotionChanged(object? sender, PropertyChangedEventArgs e) => UpdateDrift();

    private void UpdateDrift()
    {
        if (_drift is null)
            return;

        if (MotionSettings.Current.AnimationsEnabled)
            _drift.Begin(this, isControllable: true);
        else
            _drift.Stop(this);
    }
}
