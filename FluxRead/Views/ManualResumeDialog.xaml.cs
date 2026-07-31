using Flux.Ui.Views;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;

namespace FluxRead.Views;

/// <summary>Waits for the user to navigate the sender to the first missing frame, then continue.</summary>
public sealed partial class ManualResumeDialog : FluxDialog
{
    public ManualResumeDialog(uint firstMissingFrameId)
    {
        InitializeComponent();
        Title = $"Navigate to frame {firstMissingFrameId:N0}";
        DetailText.Text =
            $"Use the sender's Back or go-to-frame controls to show frame {firstMissingFrameId:N0}, then click Continue.";
    }

    /// <summary>Whether the user chose to continue (false = cancel the transfer).</summary>
    public bool Continued { get; private set; }

    private void OnOpened(ContentDialog sender, ContentDialogOpenedEventArgs args) =>
        ContinueButton.Focus(FocusState.Programmatic);

    private void OnContinue(object sender, RoutedEventArgs e)
    {
        Continued = true;
        Hide();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Hide();
}
