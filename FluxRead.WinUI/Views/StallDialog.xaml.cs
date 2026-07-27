using Flux.Ui.WinUI.Views;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;

namespace FluxRead.WinUI.Views;

/// <summary>What the user chose when a transfer stalled.</summary>
public enum StallChoice
{
    Retry,
    RecalibrateNext,
    AdjustRegion,
    Cancel,
}

/// <summary>Actionable prompt shown when the sender stops advancing.</summary>
public sealed partial class StallDialog : FluxDialog
{
    public StallDialog(string detail)
    {
        InitializeComponent();
        DetailText.Text = detail;
    }

    public StallChoice Choice { get; private set; } = StallChoice.Cancel;

    private void OnOpened(ContentDialog sender, ContentDialogOpenedEventArgs args) =>
        RetryButton.Focus(FocusState.Programmatic);

    private void OnRetry(object sender, RoutedEventArgs e) => Choose(StallChoice.Retry);

    private void OnRecalibrateNext(object sender, RoutedEventArgs e) => Choose(StallChoice.RecalibrateNext);

    private void OnAdjustRegion(object sender, RoutedEventArgs e) => Choose(StallChoice.AdjustRegion);

    private void OnCancel(object sender, RoutedEventArgs e) => Choose(StallChoice.Cancel);

    private void Choose(StallChoice choice)
    {
        Choice = choice;
        Hide();
    }
}
