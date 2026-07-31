using FluxCast.Services;
using FluxCast.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;

namespace FluxCast.Views;

public sealed partial class EncodeSetupView : UserControl
{
    public EncodeSetupViewModel Vm { get; }

    public EncodeSetupView(EncodeSetupViewModel viewModel)
    {
        Vm = viewModel;
        InitializeComponent();
    }

    /// <summary>x:Bind helper: shows the drop hint only while nothing is chosen.</summary>
    public Visibility Empty(string? value) =>
        string.IsNullOrEmpty(value) ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>x:Bind helper: shows an element only when its text is set.</summary>
    public Visibility Filled(string? value) =>
        string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>x:Bind helper: the compress box is locked for folders, which are always compressed.</summary>
    public bool Unlocked(bool locked) => !locked;

    /// <summary>x:Bind helper: a rejected source reads red, an accepted one green.</summary>
    public Brush SummaryBrush(SourceInfo? info) => (Brush)Application.Current.Resources[
        info is null or { IsValid: true } ? "SuccessBrush" : "DangerBrush"];

    /// <summary>x:Bind helper: amber for a caution, red when the pairing cannot decode.</summary>
    public Brush CautionBrush(SetupWarningLevel level) => (Brush)Application.Current.Resources[
        level == SetupWarningLevel.Severe ? "DangerBrush" : "WarningBrush"];

    private void OnSourceDragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = e.DataView.Contains(StandardDataFormats.StorageItems)
            ? DataPackageOperation.Copy
            : DataPackageOperation.None;
    }

    private async void OnSourceDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            return;

        var items = await e.DataView.GetStorageItemsAsync();
        if (items.Count > 0)
            await Vm.SelectDroppedAsync(items[0].Path);
    }
}
