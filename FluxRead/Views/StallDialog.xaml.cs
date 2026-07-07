using System.Windows;

namespace FluxRead.Views;

/// <summary>What the user chose when a transfer stalled.</summary>
public enum StallChoice
{
    Retry,
    RecalibrateNext,
    AdjustRegion,
    Cancel,
}

/// <summary>Actionable prompt shown when the sender stops advancing.</summary>
public partial class StallDialog : Window
{
    public StallDialog(string detail)
    {
        InitializeComponent();
        Flux.Ui.Controls.FluxWindowChrome.AttachCompact(this);
        DetailText.Text = detail;
    }

    public StallChoice Choice { get; private set; } = StallChoice.Cancel;

    private void OnRetry(object sender, RoutedEventArgs e) => Close(StallChoice.Retry);

    private void OnRecalibrateNext(object sender, RoutedEventArgs e) => Close(StallChoice.RecalibrateNext);

    private void OnAdjustRegion(object sender, RoutedEventArgs e) => Close(StallChoice.AdjustRegion);

    private void OnCancel(object sender, RoutedEventArgs e) => Close(StallChoice.Cancel);

    private void Close(StallChoice choice)
    {
        Choice = choice;
        DialogResult = true;
    }
}
