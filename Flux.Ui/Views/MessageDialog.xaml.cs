using System.Windows;

namespace Flux.Ui.Views;

/// <summary>
/// A themed modal replacement for the native message box. Shows a title, a message, and one or
/// two buttons; <see cref="Window.ShowDialog"/> returns true when the confirm button is chosen.
/// </summary>
public partial class MessageDialog : Window
{
    /// <summary>Creates a dialog. A null <paramref name="cancelText"/> shows only the confirm button.</summary>
    /// <param name="title">Heading text.</param>
    /// <param name="message">Body text.</param>
    /// <param name="confirmText">Label for the confirming button.</param>
    /// <param name="cancelText">Label for the dismissing button, or null for a single-button message.</param>
    /// <param name="destructive">When true the confirm button is styled as a destructive (red) action.</param>
    public MessageDialog(string title, string message, string confirmText, string? cancelText, bool destructive)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
        ConfirmButton.Style = (Style)FindResource(destructive ? "DangerButton" : "PrimaryButton");

        if (cancelText is null)
            CancelButton.Visibility = Visibility.Collapsed;
        else
            CancelButton.Content = cancelText;
    }

    private void OnConfirm(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
