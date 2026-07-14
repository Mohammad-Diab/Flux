using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace FluxCast.Views;

/// <summary>History screen listing past casts.</summary>
public partial class RecentCastsView : UserControl
{
    public RecentCastsView() => InitializeComponent();

    private void ToggleOverflow(object sender, RoutedEventArgs e)
    {
        var host = (Grid)((Button)sender).Parent;
        if (host.Children.OfType<Popup>().FirstOrDefault() is { } popup)
            popup.IsOpen = !popup.IsOpen;
    }

    private void CloseOverflow(object sender, RoutedEventArgs e)
    {
        DependencyObject node = (DependencyObject)sender;
        while (node is not null and not Popup)
            node = LogicalTreeHelper.GetParent(node);
        if (node is Popup popup)
            popup.IsOpen = false;
    }
}
