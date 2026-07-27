using System.ComponentModel;
using Flux.Ui.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Flux.Ui.WinUI.Views;

/// <summary>
/// Non-interactive layer of slow-drifting spectrum glow orbs behind the app content. It is hidden
/// entirely (its gradients not rendered, its drift stopped) when animations and effects are
/// disabled for performance, and updates live when the setting changes.
/// </summary>
public sealed partial class AmbientBackground : UserControl
{
    private Window? _window;
    private bool _running;

    public AmbientBackground()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>Watches the window so the drift can stop while it is inactive; WinUI has no
    /// <c>Window.GetWindow</c>.</summary>
    public void Attach(Window window)
    {
        _window = window;
        _window.Activated += OnWindowActivity;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        MotionSettings.Current.PropertyChanged += OnMotionChanged;
        ApplyMotionSetting();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        MotionSettings.Current.PropertyChanged -= OnMotionChanged;
        if (_window is not null)
            _window.Activated -= OnWindowActivity;

        Drift.Stop();
        _running = false;
    }

    private void OnMotionChanged(object? sender, PropertyChangedEventArgs e) => ApplyMotionSetting();

    private void OnWindowActivity(object sender, WindowActivatedEventArgs e) =>
        SyncDrift(e.WindowActivationState != WindowActivationState.Deactivated);

    private void ApplyMotionSetting()
    {
        bool enabled = MotionSettings.Current.AnimationsEnabled;
        Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;

        if (enabled)
        {
            Drift.Begin();
            _running = true;
        }
        else
        {
            Drift.Stop();
            _running = false;
        }
    }

    // Nothing off-screen needs to drift: pause the orbs whenever the window is inactive, so an idle or
    // background app draws no GPU for them.
    private void SyncDrift(bool active)
    {
        if (!_running)
            return;

        if (active)
            Drift.Resume();
        else
            Drift.Pause();
    }
}
