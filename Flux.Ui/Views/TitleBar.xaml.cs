using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Flux.Ui.Controls;

namespace Flux.Ui.Views;

/// <summary>
/// Custom window title bar for the borderless (WindowChrome) windows: brand mark, title, an
/// optional settings gear, and minimize/maximize/close controls.
/// </summary>
public partial class TitleBar : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(TitleBar), new PropertyMetadata("Flux"));

    public static readonly DependencyProperty SubtitleProperty =
        DependencyProperty.Register(nameof(Subtitle), typeof(string), typeof(TitleBar), new PropertyMetadata(""));

    public static readonly DependencyProperty BrandGeometryProperty =
        DependencyProperty.Register(nameof(BrandGeometry), typeof(Geometry), typeof(TitleBar), new PropertyMetadata(BrandMark.DefaultGlyph));

    public static readonly DependencyProperty SettingsCommandProperty =
        DependencyProperty.Register(nameof(SettingsCommand), typeof(ICommand), typeof(TitleBar), new PropertyMetadata(null));

    public static readonly DependencyProperty ShowSettingsButtonProperty =
        DependencyProperty.Register(nameof(ShowSettingsButton), typeof(bool), typeof(TitleBar), new PropertyMetadata(false));

    public static readonly DependencyProperty BackCommandProperty =
        DependencyProperty.Register(nameof(BackCommand), typeof(ICommand), typeof(TitleBar), new PropertyMetadata(null));

    public static readonly DependencyProperty ShowBackButtonProperty =
        DependencyProperty.Register(nameof(ShowBackButton), typeof(bool), typeof(TitleBar), new PropertyMetadata(false));

    private Window? _hostWindow;

    public TitleBar()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public Geometry BrandGeometry
    {
        get => (Geometry)GetValue(BrandGeometryProperty);
        set => SetValue(BrandGeometryProperty, value);
    }

    public ICommand? SettingsCommand
    {
        get => (ICommand?)GetValue(SettingsCommandProperty);
        set => SetValue(SettingsCommandProperty, value);
    }

    public bool ShowSettingsButton
    {
        get => (bool)GetValue(ShowSettingsButtonProperty);
        set => SetValue(ShowSettingsButtonProperty, value);
    }

    public ICommand? BackCommand
    {
        get => (ICommand?)GetValue(BackCommandProperty);
        set => SetValue(BackCommandProperty, value);
    }

    public bool ShowBackButton
    {
        get => (bool)GetValue(ShowBackButtonProperty);
        set => SetValue(ShowBackButtonProperty, value);
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

    private void OnMinimize(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is { } window)
            WindowChromeAnimator.Minimize(window);
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
