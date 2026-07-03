using FluxRead.ViewModels;
using FluxRead.Models;

namespace FluxRead.Views;

public partial class InputModePage : ContentPage
{
    private readonly InputModeViewModel _viewModel;

    public InputModePage(InputModeViewModel viewModel)
    {
   InitializeComponent();
      _viewModel = viewModel;
BindingContext = _viewModel;
    }

    private async void OnBrowseFolderClicked(object sender, EventArgs e)
    {
   try
        {
 // Use FolderPicker (needs platform implementation)
            var result = await DisplayPromptAsync(
          "Select Folder",
       "Enter folder path with encoded frames:",
       placeholder: @"D:\EncodedFrames");

       if (!string.IsNullOrEmpty(result))
     {
    _viewModel.SelectedFolder = result;
        }
      }
        catch (Exception ex)
        {
  await DisplayAlert("Error", $"Failed to select folder: {ex.Message}", "OK");
     }
    }

 private async void OnBrowseOutputClicked(object sender, EventArgs e)
    {
try
  {
     var result = await DisplayPromptAsync(
      "Output Folder",
     "Enter output folder path:",
    placeholder: @"D:\DecodedOutput");

   if (!string.IsNullOrEmpty(result))
     {
  _viewModel.OutputFolder = result;
          }
}
  catch (Exception ex)
  {
   await DisplayAlert("Error", $"Failed to select output folder: {ex.Message}", "OK");
    }
    }

 private async void OnCancelClicked(object sender, EventArgs e)
  {
        await Shell.Current.GoToAsync("..");
    }

    private async void OnStartClicked(object sender, EventArgs e)
    {
if (!_viewModel.Validate(out string errorMessage))
        {
     await DisplayAlert("Validation Error", errorMessage, "OK");
   return;
        }

   // For screen capture, also need output folder
 if (_viewModel.SelectedMode == InputMode.ScreenCapture)
        {
     var outputFolder = await DisplayPromptAsync(
    "Output Folder",
    "Enter output folder path for decoded file:",
  placeholder: @"D:\DecodedOutput");

            if (string.IsNullOrEmpty(outputFolder))
       {
   await DisplayAlert("Error", "Output folder is required for screen capture mode.", "OK");
    return;
  }

      _viewModel.OutputFolder = outputFolder;
        }

   // Pass data back to main page
   var parameters = new Dictionary<string, object>
     {
   { "InputMode", _viewModel.SelectedMode },
   { "SourceFolder", _viewModel.SelectedFolder ?? string.Empty },
    { "OutputFolder", _viewModel.OutputFolder ?? string.Empty },
   { "FramePeriod", _viewModel.FramePeriod }
        };

      await Shell.Current.GoToAsync("..", parameters);
    }
}
