using FluxRead.WinUI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace FluxRead.WinUI;

public sealed partial class MainWindow : Window
{
    private readonly IntPtr _hwnd;

    public FolderDecodeViewModel Vm { get; }

    public MainWindow()
    {
        Vm = App.Services.GetRequiredService<FolderDecodeViewModel>();
        InitializeComponent();

        Title = "FluxRead";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarStrip);

        _hwnd = WindowNative.GetWindowHandle(this);

        // Unpackaged WinUI pickers have no implicit parent window, so each one is bound to our HWND.
        Vm.PickFolderAsync = PickFolderAsync;
        Vm.PickSaveFileAsync = PickSaveTargetAsync;
    }

    private async Task<string?> PickFolderAsync(string _)
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, _hwnd);
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    private async Task<string?> PickSaveTargetAsync(string _, string suggestedName)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = suggestedName,
        };
        picker.FileTypeChoices.Add("File", [Path.GetExtension(suggestedName) is { Length: > 0 } ext ? ext : "."]);
        InitializeWithWindow.Initialize(picker, _hwnd);
        var file = await picker.PickSaveFileAsync();
        return file?.Path;
    }

    private void OnToggleTheme(object sender, RoutedEventArgs e)
    {
        if (Content is FrameworkElement root)
        {
            root.RequestedTheme = root.ActualTheme == ElementTheme.Dark
                ? ElementTheme.Light
                : ElementTheme.Dark;
        }
    }
}
