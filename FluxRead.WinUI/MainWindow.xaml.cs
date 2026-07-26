using FluxRead.WinUI.ViewModels;
using FluxRead.WinUI.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace FluxRead.WinUI;

public sealed partial class MainWindow : Window
{
    private readonly IntPtr _hwnd;
    private readonly FolderDecodeViewModel _folderVm;
    private readonly FolderDecodeView _folderView = new();

    public MainWindow()
    {
        _folderVm = App.Services.GetRequiredService<FolderDecodeViewModel>();
        InitializeComponent();

        Title = "FluxRead";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarStrip);

        _hwnd = WindowNative.GetWindowHandle(this);

        // Unpackaged WinUI pickers have no implicit parent window, so each one is bound to our HWND.
        _folderVm.PickFolderAsync = PickFolderAsync;
        _folderVm.PickSaveFileAsync = PickSaveTargetAsync;

        ModeHost.Content = _folderView;
    }

    private void OnTabChanged(object sender, RoutedEventArgs e)
    {
        if (ModeHost is null)
            return;

        ModeHost.Content = FolderTab.IsChecked == true
            ? _folderView
            : Placeholder(LiveTab.IsChecked == true ? "Live optical capture" : "Received");
    }

    private static UIElement Placeholder(string title) => new StackPanel
    {
        Margin = new Thickness(24, 8, 24, 24),
        Children =
        {
            new TextBlock
            {
                Text = title,
                Style = (Style)Application.Current.Resources["HeadingText"],
            },
            new TextBlock
            {
                Text = "Ported in phase 3 — needs the Interop layer.",
                Style = (Style)Application.Current.Resources["SubtleText"],
                Margin = new Thickness(0, 8, 0, 0),
            },
        },
    };

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
