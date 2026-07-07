using System.Windows;
using System.Windows.Controls;
using FluxCast.ViewModels;

namespace FluxCast.Views;

/// <summary>Setup screen view.</summary>
public partial class EncodeSetupView : UserControl
{
    public EncodeSetupView()
    {
        InitializeComponent();
    }

    private void OnSourceDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnSourceDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } paths
            && DataContext is EncodeSetupViewModel vm)
        {
            await vm.SelectDroppedAsync(paths[0]);
        }
    }
}
