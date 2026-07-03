using FluxCast.Views;
using FluxCast.Services;

namespace FluxCast;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        // Register routes
        Routing.RegisterRoute(nameof(NewStreamPage), typeof(NewStreamPage));
    }

    private async void OnNewStreamClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync(nameof(NewStreamPage));
    }

    private async void OnResumeStreamClicked(object sender, EventArgs e)
    {
        try
        {
            // Let user select encode_meta.txt file
            var customFileType = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                { DevicePlatform.WinUI, new[] { ".txt" } }
            });

            var result = await FilePicker.PickAsync(new PickOptions
            {
                PickerTitle = "Select encode_meta.txt file",
                FileTypes = customFileType
            });

            if (result == null)
                return;

            // Parse metadata
            var metadataService = new MetadataFileService();
            var metadata = metadataService.ParseMetadataFile(result.FullPath);

            if (metadata == null)
            {
                await DisplayAlert("Error", "Failed to parse metadata file. Please ensure it's a valid encode_meta.txt file.", "OK");
                return;
            }

            // Validate frames
            var validation = metadataService.ValidateFrames(metadata);

            if (validation.MissingFrames.Count > 0)
            {
                var continueAnyway = await DisplayAlert(
                    "Missing Frames",
                    $"{validation.MissingFrames.Count} frame(s) are missing.\n\n" +
                    $"Missing frames: {string.Join(", ", validation.MissingFrames.Take(10))}" +
                    (validation.MissingFrames.Count > 10 ? "..." : "") + "\n\n" +
                    "Do you want to continue anyway?",
                    "Yes", "No");

                if (!continueAnyway)
                    return;
            }

            // Navigate to main page with metadata
            await Shell.Current.GoToAsync("//MainPage", new Dictionary<string, object>
            {
                { "ResumeMetadata", metadata }
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
            "About FluxCast",
            "FluxCast - Visual Frame Encoder\n\n" +
            "Version 1.0\n\n" +
            "Encode files and folders into visual frames with error correction.",
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
