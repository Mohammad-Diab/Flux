using System.Windows;

namespace FluxRead;

/// <summary>
/// Shell window. Hosts the folder-decode screen; the live optical mode is added in a later phase.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
    }
}
