using FluxRead.Services;

namespace FluxRead;

public partial class App : Application
{
    private readonly DecoderSessionManager _sessionManager;

    public App(DecoderSessionManager sessionManager)
    {
        InitializeComponent();
        _sessionManager = sessionManager;

        MainPage = new AppShell();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = base.CreateWindow(activationState);

        // Check for incomplete sessions on startup
        window.Created += async (s, e) =>
        {
            await Task.Delay(1000); // Brief delay for UI initialization

            var tempFolder = Path.Combine(Path.GetTempPath(), "FluxRead");
            var incompleteSessions = _sessionManager.GetIncompleteSessionFiles(tempFolder);

            if (incompleteSessions.Any())
            {
                var resume = await MainPage.DisplayAlert(
                    "Resume Incomplete Session?",
                    $"Found {incompleteSessions.Count} incomplete decoding session(s).\n\n" +
                    "Would you like to resume the most recent one?",
                    "Resume", "Ignore");

                if (resume)
                {
                    await Shell.Current.GoToAsync("//MainPage", new Dictionary<string, object>
                    {
                        { "ResumeProgressFile", incompleteSessions.First() }
                    });
                }
            }
        };

        return window;
    }
}
