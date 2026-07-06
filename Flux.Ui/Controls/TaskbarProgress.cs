using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Shell;

namespace Flux.Ui.Controls;

/// <summary>Drives the taskbar button's progress bar. Shared via the "TaskbarProgress" app resource; windows bind TaskbarItemInfo to it and view models push updates.</summary>
public sealed class TaskbarProgress : INotifyPropertyChanged
{
    private static readonly Lazy<TaskbarProgress> Fallback = new(() => new TaskbarProgress());
    private TaskbarItemProgressState _state = TaskbarItemProgressState.None;
    private double _value;

    /// <summary>The shared instance held in application resources.</summary>
    public static TaskbarProgress Current =>
        Application.Current?.TryFindResource("TaskbarProgress") as TaskbarProgress ?? Fallback.Value;

    public TaskbarItemProgressState State
    {
        get => _state;
        private set { if (_state != value) { _state = value; Raise(nameof(State)); } }
    }

    public double Value
    {
        get => _value;
        private set { if (_value != value) { _value = value; Raise(nameof(Value)); } }
    }

    /// <summary>Shows a determinate bar at <paramref name="fraction"/> (0-1).</summary>
    public void Report(double fraction)
    {
        Value = Math.Clamp(fraction, 0, 1);
        State = TaskbarItemProgressState.Normal;
    }

    /// <summary>Shows an indeterminate (marching) bar.</summary>
    public void Indeterminate() => State = TaskbarItemProgressState.Indeterminate;

    /// <summary>Hides the taskbar progress.</summary>
    public void Clear()
    {
        State = TaskbarItemProgressState.None;
        Value = 0;
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise(string property) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
