using System.Windows;

namespace FluxRead.Views;

/// <summary>Waits for the user to navigate the sender to the first missing frame, then continue.</summary>
public partial class ManualResumeDialog : Window
{
    public ManualResumeDialog(uint firstMissingFrameId)
    {
        InitializeComponent();
        Flux.Ui.Controls.FluxWindowChrome.AttachCompact(this);
        TitleText.Text = $"Navigate to frame {firstMissingFrameId:N0}";
        DetailText.Text =
            $"Use the sender's Back or go-to-frame controls to show frame {firstMissingFrameId:N0}, then click Continue.";
    }

    /// <summary>Whether the user chose to continue (false = cancel the transfer).</summary>
    public bool Continued { get; private set; }

    private void OnContinue(object sender, RoutedEventArgs e)
    {
        Continued = true;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = true;
}
