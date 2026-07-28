using FluxCast.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FluxCast.WinUI.Views;

public sealed partial class RecentCastsView : UserControl
{
    public RecentCastsViewModel Vm { get; }

    public RecentCastsView(RecentCastsViewModel viewModel)
    {
        Vm = viewModel;
        InitializeComponent();
    }

    /// <summary>x:Bind helper: the inverse of a bool-to-Visibility binding.</summary>
    public Visibility Hidden(bool value) => value ? Visibility.Collapsed : Visibility.Visible;
}
