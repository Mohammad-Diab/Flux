using FluxRead.WinUI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace FluxRead.WinUI.Views;

public sealed partial class SettingsView : UserControl
{
    public SettingsViewModel Vm { get; } = App.Services.GetRequiredService<SettingsViewModel>();

    public SettingsView() => InitializeComponent();
}
