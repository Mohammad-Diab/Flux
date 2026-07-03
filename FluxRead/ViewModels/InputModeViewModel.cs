using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluxRead.Models;

namespace FluxRead.ViewModels;

/// <summary>
/// ViewModel for input mode selection dialog.
/// </summary>
public partial class InputModeViewModel : ObservableObject
{
    [ObservableProperty]
    private InputMode _selectedMode = InputMode.Folder;

    [ObservableProperty]
    private string? _selectedFolder;

    [ObservableProperty]
    private string? _outputFolder;

    [ObservableProperty]
    private double _framePeriod = 2.0;

    public bool IsFolderMode => SelectedMode == InputMode.Folder;
    public bool IsScreenCaptureMode => SelectedMode == InputMode.ScreenCapture;

    partial void OnSelectedModeChanged(InputMode value)
    {
     OnPropertyChanged(nameof(IsFolderMode));
        OnPropertyChanged(nameof(IsScreenCaptureMode));
    }

    public bool Validate(out string errorMessage)
    {
if (SelectedMode == InputMode.Folder)
        {
       if (string.IsNullOrEmpty(SelectedFolder))
{
   errorMessage = "Please select a folder containing frames.";
      return false;
         }

 if (!Directory.Exists(SelectedFolder))
{
     errorMessage = "Selected folder does not exist.";
     return false;
   }

            if (string.IsNullOrEmpty(OutputFolder))
    {
              errorMessage = "Please select an output folder.";
        return false;
       }
        }
      else // ScreenCapture mode
        {
    if (FramePeriod < 0.1 || FramePeriod > 60)
       {
            errorMessage = "Frame period must be between 0.1 and 60 seconds.";
     return false;
    }
  }

        errorMessage = string.Empty;
     return true;
    }
}
