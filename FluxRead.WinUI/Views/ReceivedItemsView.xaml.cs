using FluxRead.WinUI.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace FluxRead.WinUI.Views;

public sealed partial class ReceivedItemsView : UserControl
{
    public ReceivedItemsViewModel Vm { get; }

    public ReceivedItemsView(ReceivedItemsViewModel viewModel)
    {
        Vm = viewModel;
        InitializeComponent();
    }
}
