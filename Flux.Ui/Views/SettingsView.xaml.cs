using System.Windows.Controls;

namespace Flux.Ui.Views;

/// <summary>Settings screen (appearance + motion). DataContext is a SettingsViewModel.</summary>
public partial class SettingsView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsView"/> class.
    /// </summary>
    public SettingsView()
    {
        InitializeComponent();
    }
}
