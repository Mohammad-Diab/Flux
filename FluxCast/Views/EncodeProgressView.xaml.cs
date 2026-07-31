using FluxCast.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FluxCast.Views;

public sealed partial class EncodeProgressView : UserControl
{
    public EncodeProgressViewModel Vm { get; }

    public EncodeProgressView(EncodeProgressViewModel viewModel)
    {
        Vm = viewModel;
        InitializeComponent();
    }

    /// <summary>x:Bind helper: shows an element only when its text is set.</summary>
    public Visibility Filled(string? value) =>
        string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>x:Bind helper: the determinate bar and the marching one swap places.</summary>
    public Visibility Determinate(bool indeterminate) =>
        indeterminate ? Visibility.Collapsed : Visibility.Visible;
}
