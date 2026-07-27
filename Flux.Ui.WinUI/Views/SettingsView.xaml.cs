using Flux.Ui.WinUI.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Flux.Ui.WinUI.Views;

public sealed partial class SettingsView : UserControl
{
    public SettingsViewModel Vm { get; }

    public SettingsView(SettingsViewModel viewModel)
    {
        Vm = viewModel;
        InitializeComponent();
    }
}
