using System;
using System.ComponentModel;
using System.Windows;
using FluxCast.ViewModels;

namespace FluxCast;

/// <summary>
/// Shell window. The window is always resizable; the presenter scales the frame image to fit
/// (nearest-neighbor, aspect-locked), which the capture-tolerant decoder handles at any size.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
        Flux.Ui.Controls.WindowChromeAnimator.Attach(this, RootContent);
        DataContextChanged += OnDataContextChanged;
    }

    /// <inheritdoc/>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Flux.Ui.Controls.NativeChrome.EnableWindowAnimations(this);
        Flux.Ui.Controls.Win11Corners.Apply(this);
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
        // Grow to a comfortable size for presenting if the window is still form-sized, but keep
        // it freely resizable so the user can enlarge the frame for a more robust capture.
        if (viewModel is PresenterViewModel && Width < 1200)
        {
            Width = 1280;
            Height = 900;
            CenterOnScreen();
        }
    }

    private void CenterOnScreen()
    {
        Left = SystemParameters.WorkArea.Left + (SystemParameters.WorkArea.Width - Width) / 2;
        Top = SystemParameters.WorkArea.Top + (SystemParameters.WorkArea.Height - Height) / 2;
    }
}
