using Windows.Storage.Pickers;
using WinRT.Interop;

namespace FluxCast.Services;

public partial class FolderPicker
{
    public partial async Task<FolderPickerResult> PickAsync(CancellationToken cancellationToken = default)
    {
        var picker = new Windows.Storage.Pickers.FolderPicker();

        // Get the window handle for .NET MAUI
        var window = Application.Current?.Windows[0]?.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
        if (window != null)
        {
            var hwnd = WindowNative.GetWindowHandle(window);
            InitializeWithWindow.Initialize(picker, hwnd);
        }

        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeFilter.Add("*");

        var folder = await picker.PickSingleFolderAsync();

        if (folder != null)
        {
            return FolderPickerResult.Success(new StorageFolder { Path = folder.Path });
        }

        return FolderPickerResult.Cancelled();
    }
}
