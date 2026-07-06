using System.Windows;
using FluxRead.ViewModels;

namespace FluxRead.Views;

/// <summary>
/// Compact always-on-top window shown during a live transfer so a single-screen user can watch
/// FluxCast and the transfer status at once. Shows state, progress, the last capture, and
/// pause/cancel controls.
/// </summary>
public partial class MiniCaptureWindow : Window
{
    private readonly Action _onPauseToggle;
    private readonly Action _onCancel;

    /// <summary>
    /// Initializes a new instance of the <see cref="MiniCaptureWindow"/> class.
    /// </summary>
    /// <param name="viewModel">Shared live-capture status view model.</param>
    /// <param name="onPauseToggle">Invoked when the pause/resume button is clicked.</param>
    /// <param name="onCancel">Invoked when cancel is clicked.</param>
    public MiniCaptureWindow(LiveCaptureViewModel viewModel, Action onPauseToggle, Action onCancel)
    {
        _onPauseToggle = onPauseToggle;
        _onCancel = onCancel;
        DataContext = viewModel;
        InitializeComponent();

        // Park in the bottom-right of the working area so it stays out of the capture region.
        Loaded += (_, _) =>
        {
            Left = SystemParameters.WorkArea.Right - Width - 24;
            Top = SystemParameters.WorkArea.Bottom - Height - 24;
        };
    }

    /// <inheritdoc/>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Flux.Ui.Controls.Win11Corners.Apply(this);
    }

    private void OnPauseToggle(object sender, RoutedEventArgs e) => _onPauseToggle();

    private void OnCancel(object sender, RoutedEventArgs e) => _onCancel();
}
