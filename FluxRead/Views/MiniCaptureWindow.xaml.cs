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

    public MiniCaptureWindow(LiveCaptureViewModel viewModel, Action onPauseToggle, Action onCancel)
    {
        _onPauseToggle = onPauseToggle;
        _onCancel = onCancel;
        DataContext = viewModel;
        InitializeComponent();
        Flux.Ui.Controls.FluxWindowChrome.AttachCompact(this);

        // Park in the bottom-right of the working area so it stays out of the capture region.
        Loaded += (_, _) =>
        {
            Left = SystemParameters.WorkArea.Right - Width - 24;
            Top = SystemParameters.WorkArea.Bottom - Height - 24;
        };
    }

    private void OnPauseToggle(object sender, RoutedEventArgs e) => _onPauseToggle();

    private void OnCancel(object sender, RoutedEventArgs e) => _onCancel();
}
