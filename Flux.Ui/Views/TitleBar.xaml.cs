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
    // Default brand mark: a downward "receive" arrow (FluxRead); FluxCast overrides it.
    private static readonly Geometry DefaultBrand = Geometry.Parse("M7,13 L13,7 L9.5,7 L9.5,1 L4.5,1 L4.5,7 L1,7 Z");

    /// <summary>Identifies the <see cref="Title"/> property.</summary>
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(TitleBar), new PropertyMetadata("Flux"));

    /// <summary>Identifies the <see cref="Subtitle"/> property.</summary>
    public static readonly DependencyProperty SubtitleProperty =
        DependencyProperty.Register(nameof(Subtitle), typeof(string), typeof(TitleBar), new PropertyMetadata(""));

    /// <summary>Identifies the <see cref="BrandGeometry"/> property.</summary>
    public static readonly DependencyProperty BrandGeometryProperty =
        DependencyProperty.Register(nameof(BrandGeometry), typeof(Geometry), typeof(TitleBar), new PropertyMetadata(DefaultBrand));

    /// <summary>Identifies the <see cref="SettingsCommand"/> property.</summary>
    public static readonly DependencyProperty SettingsCommandProperty =
        DependencyProperty.Register(nameof(SettingsCommand), typeof(ICommand), typeof(TitleBar), new PropertyMetadata(null));

    /// <summary>Identifies the <see cref="ShowSettingsButton"/> property.</summary>
    public static readonly DependencyProperty ShowSettingsButtonProperty =
        DependencyProperty.Register(nameof(ShowSettingsButton), typeof(bool), typeof(TitleBar), new PropertyMetadata(false));

    /// <summary>Identifies the <see cref="BackCommand"/> property.</summary>
    public static readonly DependencyProperty BackCommandProperty =
        DependencyProperty.Register(nameof(BackCommand), typeof(ICommand), typeof(TitleBar), new PropertyMetadata(null));

    /// <summary>Identifies the <see cref="ShowBackButton"/> property.</summary>
    public static readonly DependencyProperty ShowBackButtonProperty =
        DependencyProperty.Register(nameof(ShowBackButton), typeof(bool), typeof(TitleBar), new PropertyMetadata(false));

    private Window? _hostWindow;

    /// <summary>
    /// Initializes a new instance of the <see cref="TitleBar"/> class.
    /// </summary>
    public TitleBar()
    {
        InitializeComponent();
        Loaded += OnLoaded;
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

    /// <summary>Gets or sets the brand-mark vector geometry.</summary>
    public Geometry BrandGeometry
    {
        get => (Geometry)GetValue(BrandGeometryProperty);
        set => SetValue(BrandGeometryProperty, value);
    }

    /// <summary>Gets or sets the command invoked by the settings gear.</summary>
    public ICommand? SettingsCommand
    {
        get => (ICommand?)GetValue(SettingsCommandProperty);
        set => SetValue(SettingsCommandProperty, value);
    }

    /// <summary>Gets or sets whether the settings gear is shown.</summary>
    public bool ShowSettingsButton
    {
        get => (bool)GetValue(ShowSettingsButtonProperty);
        set => SetValue(ShowSettingsButtonProperty, value);
    }

    /// <summary>Gets or sets the command invoked by the back button.</summary>
    public ICommand? BackCommand
    {
        get => (ICommand?)GetValue(BackCommandProperty);
        set => SetValue(BackCommandProperty, value);
    }

    /// <summary>Gets or sets whether the back button is shown (e.g. on a nested page).</summary>
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
