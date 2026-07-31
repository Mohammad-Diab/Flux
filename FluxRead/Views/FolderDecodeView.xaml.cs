using FluxRead.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace FluxRead.Views;

public sealed partial class FolderDecodeView : UserControl
{
    public FolderDecodeViewModel Vm { get; } = App.Services.GetRequiredService<FolderDecodeViewModel>();

    public FolderDecodeView() => InitializeComponent();
}
