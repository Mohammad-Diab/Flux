using System;
using System.Windows;
using System.Windows.Controls;

namespace FluxCast.Views;

/// <summary>
/// Custom window title bar for the borderless (WindowChrome) windows: brand mark, title, and
/// minimize/maximize/close controls.
/// </summary>
public partial class TitleBar : UserControl
{
    /// <summary>Identifies the <see cref="Title"/> property.</summary>
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(TitleBar), new PropertyMetadata("FluxCast"));

    /// <summary>Identifies the <see cref="Subtitle"/> property.</summary>
    public static readonly DependencyProperty SubtitleProperty =
        DependencyProperty.Register(nameof(Subtitle), typeof(string), typeof(TitleBar), new PropertyMetadata(""));

    /// <summary>
    /// Initializes a new instance of the <see cref="TitleBar"/> class.
    /// </summary>
    private Window? _hostWindow;

    public TitleBar()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _hostWindow = Window.GetWindow(this);
        if (_hostWindow is not null)
        {
            _hostWindow.StateChanged -= OnHostStateChanged;
            _hostWindow.StateChanged += OnHostStateChanged;
            UpdateMaxState();
        }
    }

    private void OnHostStateChanged(object? sender, EventArgs e) => UpdateMaxState();

    // Swap the maximize/restore glyph (and tooltip) to reflect the current window state.
    private void UpdateMaxState()
    {
        bool maximized = _hostWindow?.WindowState == WindowState.Maximized;
        MaxGlyph.Visibility = maximized ? Visibility.Collapsed : Visibility.Visible;
        RestoreGlyph.Visibility = maximized ? Visibility.Visible : Visibility.Collapsed;
        MaxButton.ToolTip = maximized ? "Restore" : "Maximize";
    }

    /// <summary>Gets or sets the title text.</summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>Gets or sets the subtitle text shown next to the title.</summary>
    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    private void OnMinimize(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is { } window)
            Controls.WindowChromeAnimator.Minimize(window);
    }

    private void OnMaximize(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is { } window)
        {
            window.WindowState = window.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Window.GetWindow(this)?.Close();
}
