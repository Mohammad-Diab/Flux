using System.Windows;
using System.Windows.Controls;

namespace FluxRead.Views;

/// <summary>
/// Folder-decode screen view.
/// </summary>
public partial class FolderDecodeView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FolderDecodeView"/> class.
    /// </summary>
    public FolderDecodeView()
    {
        InitializeComponent();
    }

    private void OnOpenDevTools(object sender, RoutedEventArgs e) =>
        new InteropDevWindow { Owner = Window.GetWindow(this) }.Show();
}
