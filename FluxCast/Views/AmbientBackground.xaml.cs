using System.Windows.Controls;

namespace FluxCast.Views;

/// <summary>
/// Non-interactive layer of slow-drifting spectrum glow orbs behind the app content —
/// the ambient motion that gives the UI life without distracting from work.
/// </summary>
public partial class AmbientBackground : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AmbientBackground"/> class.
    /// </summary>
    public AmbientBackground()
    {
        InitializeComponent();
    }
}
