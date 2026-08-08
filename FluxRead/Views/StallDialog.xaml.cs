using Flux.Ui.Views;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;

namespace FluxRead.Views;

/// <summary>What the user chose when a transfer stalled.</summary>
public enum StallChoice
{
    Retry,
    Manual,
    Stop,
}

/// <summary>
/// Actionable prompt shown when the loop has diagnosed a failure and run out of automatic
/// retries. Title, detail, and the manual-calibration action are cause-specific; the manual
/// button is hidden for causes with no manual remedy (unexpected errors).
/// </summary>
public sealed partial class StallDialog : FluxDialog
{
    public StallDialog(string title, string detail, string? manualLabel = null, string? manualHint = null)
    {
        InitializeComponent();
        Title = title;
        DetailText.Text = detail;
        if (manualLabel is null)
        {
            ManualButton.Visibility = Visibility.Collapsed;
            ManualHint.Visibility = Visibility.Collapsed;
        }
        else
        {
            ManualButton.Content = manualLabel;
            ManualHint.Text = manualHint ?? "";
        }
    }

    public StallChoice Choice { get; private set; } = StallChoice.Stop;

    private void OnOpened(ContentDialog sender, ContentDialogOpenedEventArgs args) =>
        RetryButton.Focus(FocusState.Programmatic);

    private void OnRetry(object sender, RoutedEventArgs e) => Choose(StallChoice.Retry);

    private void OnManual(object sender, RoutedEventArgs e) => Choose(StallChoice.Manual);

    private void OnStop(object sender, RoutedEventArgs e) => Choose(StallChoice.Stop);

    private void Choose(StallChoice choice)
    {
        Choice = choice;
        Hide();
    }
}
