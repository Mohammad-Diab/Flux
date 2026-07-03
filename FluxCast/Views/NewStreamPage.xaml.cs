using FluxCast.ViewModels;
using FluxCast.Services;
using FluxCore.Framing;
using Microsoft.Extensions.Logging;

namespace FluxCast.Views;

public partial class NewStreamPage : ContentPage
{
    private readonly NewStreamViewModel _viewModel;
    private readonly IFolderPicker _folderPicker;
    private readonly InputValidationService _validationService;
  private readonly ILogger<NewStreamPage>? _logger;

    public NewStreamPage(
 NewStreamViewModel viewModel, 
        IFolderPicker folderPicker, 
   InputValidationService validationService,
        ILogger<NewStreamPage>? logger = null)
    {
InitializeComponent();
_viewModel = viewModel;
        _folderPicker = folderPicker;
    _validationService = validationService;
     _logger = logger;
        BindingContext = _viewModel;
    }

    private async void OnSelectInputClicked(object sender, EventArgs e)
    {
        var action = await DisplayActionSheet(
               "Select Input Type",
                  "Cancel",
                  null,
                  "Select File",
                  "Select Folder");

        switch (action)
        {
            case "Select File":
                await SelectFileAsync();
                break;
            case "Select Folder":
                await SelectFolderAsync();
                break;
        }
    }

    private async Task SelectFileAsync()
    {
        try
        {
            var result = await FilePicker.PickAsync(new PickOptions
            {
                PickerTitle = "Select a file to encode"
            });

            if (result != null)
            {
                // Validate the selected file
                var validation = await _validationService.ValidateInputAsync(
                    result.FullPath,
                    isFolder: false,
                    _viewModel.EnableCompression);

                if (!validation.IsValid)
                {
                    await DisplayAlert("Validation Error", validation.ErrorMessage ?? "Invalid file", "OK");
                    return;
                }

                // Show warning if any
                if (!string.IsNullOrEmpty(validation.WarningMessage))
                {
                    await DisplayAlert("Warning", validation.WarningMessage, "OK");
                }

                // Set the path
                _viewModel.SelectedPath = result.FullPath;
                _viewModel.IsFolder = false;

                // Show info if available
                if (!string.IsNullOrEmpty(validation.InfoMessage))
                {
                    _logger?.LogInformation(validation.InfoMessage);
                }

                // Estimate frame count
                var estimatedFrames = await _validationService.EstimateFrameCountAsync(
                    result.FullPath,
                    isFolder: false,
                    _viewModel.EnableCompression,
                    (int)_viewModel.SelectedTileSize,
                    _viewModel.EccLevel);

                if (estimatedFrames > 0)
                {
                    await DisplayAlert("Estimate",
                        $"Estimated frames: ~{estimatedFrames}\n" +
                        $"(This is approximate and may vary based on compression)",
                        "OK");
                }
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to select file: {ex.Message}", "OK");
        }
    }

    private async Task SelectFolderAsync()
    {
        try
        {
            var result = await _folderPicker.PickAsync();

            if (result.IsSuccessful && result.Folder != null)
            {
                // Validate the selected folder
                var validation = await _validationService.ValidateInputAsync(
                    result.Folder.Path,
                    isFolder: true,
                    requireCompression: true); // Folders always require compression

                if (!validation.IsValid)
                {
                    await DisplayAlert("Validation Error", validation.ErrorMessage ?? "Invalid folder", "OK");
                    return;
                }

                // Show warning if any
                if (!string.IsNullOrEmpty(validation.WarningMessage))
                {
                    await DisplayAlert("Warning", validation.WarningMessage, "OK");
                }

                // Set the path
                _viewModel.SelectedPath = result.Folder.Path;
                _viewModel.IsFolder = true;

                // Show info if available
                if (!string.IsNullOrEmpty(validation.InfoMessage))
                {
                    _logger?.LogInformation(validation.InfoMessage);
                }

                // Estimate frame count
                var estimatedFrames = await _validationService.EstimateFrameCountAsync(
                    result.Folder.Path,
                    isFolder: true,
                    enableCompression: true,
                    (int)_viewModel.SelectedTileSize,
                    _viewModel.EccLevel);

                if (estimatedFrames > 0)
                {
                    await DisplayAlert("Estimate",
                        $"Estimated frames: ~{estimatedFrames}\n" +
                        $"(This is approximate and may vary based on compression)",
                        "OK");
                }
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to select folder: {ex.Message}", "OK");
        }
    }

    private async void OnBrowseDestinationClicked(object sender, EventArgs e)
    {
        try
        {
            var result = await _folderPicker.PickAsync();

            if (result.IsSuccessful && result.Folder != null)
            {
                _viewModel.DestinationFolder = result.Folder.Path;
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to select destination: {ex.Message}", "OK");
        }
    }

    private async void OnCancelClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }

    private async void OnStartEncodingClicked(object sender, EventArgs e)
    {
        if (!_viewModel.Validate(out string errorMessage))
        {
            await DisplayAlert("Validation Error", errorMessage, "OK");
            return;
        }

        // Map tile size index to enum
        var tileSizeIndex = TileSizePicker.SelectedIndex;
        var tileSize = tileSizeIndex switch
        {
            0 => TileSize.Size2x2,
            1 => TileSize.Size4x4,
            2 => TileSize.Size6x6,
            3 => TileSize.Size8x8,
            _ => TileSize.Size8x8
        };
        _viewModel.SelectedTileSize = tileSize;

        // Pass configuration to main page and navigate
        var config = new StreamConfiguration
        {
            SelectedPath = _viewModel.SelectedPath!,
            IsFolder = _viewModel.IsFolder,
            TileSize = _viewModel.SelectedTileSize,
            EccLevel = _viewModel.EccLevel,
            EnableCompression = _viewModel.EnableCompression,
            ExportDirectly = _viewModel.ExportDirectly,
            DestinationFolder = _viewModel.DestinationFolder!,
            AutoPlay = _viewModel.AutoPlay,
            FramePeriod = _viewModel.FramePeriod
        };

        await Shell.Current.GoToAsync("..", new Dictionary<string, object>
        {
      { "Configuration", config }
        });
    }
}

/// <summary>
/// Configuration for a new encoding stream.
/// </summary>
public class StreamConfiguration
{
    public string SelectedPath { get; set; } = string.Empty;
    public bool IsFolder { get; set; }
    public TileSize TileSize { get; set; }
    public int EccLevel { get; set; }
    public bool EnableCompression { get; set; }
    public bool ExportDirectly { get; set; }
    public string DestinationFolder { get; set; } = string.Empty;
    public bool AutoPlay { get; set; }
    public double FramePeriod { get; set; }
}
