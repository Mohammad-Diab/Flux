using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FluxRead.WinUI.Views;

/// <summary>How the user chose to resume an interrupted reception.</summary>
public enum ResumeChoice
{
    Automatic,
    Manual,
    StartOver,
    Cancel,
}

/// <summary>Prompt shown when a returning transfer matches a partially received one.</summary>
public sealed partial class ResumeDialog : FluxDialog
{
    public ResumeDialog(int receivedFrames, int expectedFrames, uint firstMissingFrameId)
    {
        InitializeComponent();
        DetailText.Text =
            $"You've already received {receivedFrames:N0} of {expectedFrames:N0} frames. "
            + $"Pick how to reach frame {firstMissingFrameId:N0} — the first one still missing.";
        AutomaticHint.Text = $"FluxRead clicks Next to skip ahead to frame {firstMissingFrameId:N0}, then keeps going.";
        ManualHint.Text = $"Show frame {firstMissingFrameId:N0} on the sender yourself, then click Continue.";
        StartOverHint.Text = $"Discard the {receivedFrames:N0} frames and receive from the beginning.";
    }

    public ResumeChoice Choice { get; private set; } = ResumeChoice.Cancel;

    private void OnOpened(ContentDialog sender, ContentDialogOpenedEventArgs args) =>
        AutomaticButton.Focus(FocusState.Programmatic);

    private void OnAutomatic(object sender, RoutedEventArgs e) => Choose(ResumeChoice.Automatic);

    private void OnManual(object sender, RoutedEventArgs e) => Choose(ResumeChoice.Manual);

    private void OnStartOver(object sender, RoutedEventArgs e) => Choose(ResumeChoice.StartOver);

    private void OnCancel(object sender, RoutedEventArgs e) => Choose(ResumeChoice.Cancel);

    private void Choose(ResumeChoice choice)
    {
        Choice = choice;
        Hide();
    }
}
