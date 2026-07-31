using FluxRead.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace FluxRead.Views;

public sealed partial class ReceivedItemsView : UserControl
{
    public ReceivedItemsViewModel Vm { get; }

    public ReceivedItemsView(ReceivedItemsViewModel viewModel)
    {
        Vm = viewModel;
        InitializeComponent();
    }
}
