using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using FluxCast.ViewModels;
using FluxCore.Framing;

namespace FluxCast;

/// <summary>
/// Shell window. While presenting, the window is sized so the frame fits at exactly
/// 1:1 physical pixels and resizing is locked — FluxRead's calibration depends on the
/// window staying put.
/// </summary>
public partial class MainWindow : Window
{
    private const double PresenterBarHeight = 110;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is ShellViewModel oldShell)
            oldShell.PropertyChanged -= OnShellPropertyChanged;

        if (e.NewValue is ShellViewModel shell)
        {
            shell.PropertyChanged += OnShellPropertyChanged;
            ApplyModeFor(shell.Current);
        }
    }

    private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.Current) && sender is ShellViewModel shell)
            ApplyModeFor(shell.Current);
    }

    private void ApplyModeFor(object? viewModel)
    {
        if (viewModel is PresenterViewModel)
            EnterPresenterMode();
        else
            EnterNormalMode();
    }

    private void EnterPresenterMode()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        double contentWidth = FrameFormat.FrameWidthPx / dpi.DpiScaleX + 24;
        double contentHeight = FrameFormat.FrameHeightPx / dpi.DpiScaleY + PresenterBarHeight + 12;

        Width = contentWidth + SystemParameters.WindowResizeBorderThickness.Left * 2;
        Height = contentHeight + SystemParameters.WindowCaptionHeight +
                 SystemParameters.WindowResizeBorderThickness.Top * 2;
        ResizeMode = ResizeMode.NoResize;
        CenterOnScreen();
    }

    private void EnterNormalMode()
    {
        ResizeMode = ResizeMode.CanResize;
        Width = 960;
        Height = 640;
        CenterOnScreen();
    }

    private void CenterOnScreen()
    {
        Left = (SystemParameters.WorkArea.Width - Width) / 2 + SystemParameters.WorkArea.Left;
        Top = (SystemParameters.WorkArea.Height - Height) / 2 + SystemParameters.WorkArea.Top;
    }
}
