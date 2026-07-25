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
    private Window? _window;
    private bool _running;

    public AmbientBackground()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        IsVisibleChanged += (_, _) => SyncDrift();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _drift ??= (Storyboard)Resources["Drift"];
        MotionSettings.Current.PropertyChanged += OnMotionChanged;

        _window = Window.GetWindow(this);
        if (_window is not null)
        {
            _window.Activated += OnWindowActivity;
            _window.Deactivated += OnWindowActivity;
            _window.StateChanged += OnWindowActivity;
        }

        ApplyMotionSetting();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        MotionSettings.Current.PropertyChanged -= OnMotionChanged;
        if (_window is not null)
        {
            _window.Activated -= OnWindowActivity;
            _window.Deactivated -= OnWindowActivity;
            _window.StateChanged -= OnWindowActivity;
        }
        _drift?.Stop(this);
        _running = false;
    }

    private void OnMotionChanged(object? sender, PropertyChangedEventArgs e) => ApplyMotionSetting();

    private void OnWindowActivity(object? sender, EventArgs e) => SyncDrift();

    private void ApplyMotionSetting()
    {
        bool enabled = MotionSettings.Current.AnimationsEnabled;
        Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;

        if (_drift is null)
            return;
        if (enabled)
        {
            _drift.Begin(this, isControllable: true);
            _running = true;
            SyncDrift();
        }
        else
        {
            _drift.Stop(this);
            _running = false;
        }
    }

    // Nothing off-screen needs to drift: pause the orbs whenever the window is inactive, minimized, or
    // hidden so an idle or background app draws no GPU for them.
    private void SyncDrift()
    {
        if (_drift is null || !_running)
            return;

        bool visible = IsVisible && _window is { IsActive: true } && _window.WindowState != WindowState.Minimized;
        if (visible)
            _drift.Resume(this);
        else
            _drift.Pause(this);
    }
}
