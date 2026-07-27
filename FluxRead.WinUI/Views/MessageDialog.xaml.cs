using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FluxRead.WinUI.Views;

/// <summary>Themed replacement for the native message box: a title, a message, and one or two buttons.</summary>
public sealed partial class MessageDialog : FluxDialog
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
        Title = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
        ConfirmButton.Style = (Style)Application.Current.Resources[destructive ? "DangerButton" : "PrimaryButton"];

        if (cancelText is null)
            CancelButton.Visibility = Visibility.Collapsed;
        else
            CancelButton.Content = cancelText;
    }

    /// <summary>Whether the confirming button was chosen; cancel and Escape both leave it false.</summary>
    public bool Confirmed { get; private set; }

    // Focus the confirm button so Enter confirms, as IsDefault did in WPF.
    private void OnOpened(ContentDialog sender, ContentDialogOpenedEventArgs args) =>
        ConfirmButton.Focus(FocusState.Programmatic);

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        Confirmed = true;
        Hide();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Hide();
}
