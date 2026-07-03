using FluxRead.Views;

namespace FluxRead;

public partial class AppShell : Shell
{
    public AppShell()
 {
        InitializeComponent();

        // Register routes
     Routing.RegisterRoute(nameof(InputModePage), typeof(InputModePage));
    }

    private async void OnStartReadingClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync(nameof(InputModePage));
 }

  private async void OnResumeSessionClicked(object sender, EventArgs e)
    {
 try
    {
          // Let user select decoder_progress.json file
  var customFileType = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
  {
      { DevicePlatform.WinUI, new[] { ".json" } }
   });

 var result = await FilePicker.PickAsync(new PickOptions
            {
         PickerTitle = "Select decoder_progress.json file",
     FileTypes = customFileType
  });

 if (result == null)
   return;

       // Navigate to main page with resume command
       await Shell.Current.GoToAsync("//MainPage", new Dictionary<string, object>
      {
  { "ResumeProgressFile", result.FullPath }
   });
      }
 catch (Exception ex)
  {
   await DisplayAlert("Error", $"Failed to resume session: {ex.Message}", "OK");
      }
 }

 private void OnExitClicked(object sender, EventArgs e)
    {
        Application.Current?.Quit();
    }

 private async void OnAboutClicked(object sender, EventArgs e)
    {
      await DisplayAlert(
        "About FluxRead",
       "FluxRead - Visual Frame Decoder\n\n" +
        "Version 1.0\n\n" +
            "Decode files from visual frames with error correction.",
   "OK");
    }

   private async void OnDocumentationClicked(object sender, EventArgs e)
    {
   await DisplayAlert(
    "Documentation",
       "For documentation and help, visit:\n" +
       "https://github.com/Mohammad-Diab/Flux",
    "OK");
    }
}
