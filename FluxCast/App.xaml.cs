using FluxCast.Services;

namespace FluxCast;

public partial class App : Application
{
    private readonly SessionManager _sessionManager;

    public App(SessionManager sessionManager)
    {
        InitializeComponent();
        _sessionManager = sessionManager;

        MainPage = new AppShell();
    }

    protected override async void OnStart()
    {
        base.OnStart();

        // Check for incomplete sessions
        var incompleteSessions = _sessionManager.GetIncompleteSessions();

        if (incompleteSessions.Any())
        {
            var session = incompleteSessions.First();
            var resume = await MainPage.DisplayAlert(
                "Resume Session?",
                $"Found incomplete encoding session:\n\n" +
                $"Source: {session.EncodingConfig.SourcePath}\n" +
                $"Progress: {session.Progress.EncodedFrames}/{session.Progress.TotalFrames} frames\n" +
                $"Started: {session.Progress.StartTime:g}\n\n" +
                "Would you like to resume?",
                "Resume", "Discard");

            if (resume)
            {
                // Navigate to main page with session
                await Shell.Current.GoToAsync("//MainPage", new Dictionary<string, object>
                {
                    { "ResumeSession", session }
                });
            }
            else
            {
                // Cleanup session
                await _sessionManager.CleanupSessionAsync(session.SessionId);
            }
        }
    }
}
