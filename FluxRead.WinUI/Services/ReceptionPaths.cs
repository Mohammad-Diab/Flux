namespace FluxRead.WinUI.Services;

/// <summary>Where partially received transfers live, shared with the WPF app so a reception
/// started in one can be resumed in the other.</summary>
public static class ReceptionPaths
{
    public static string SessionRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Flux", "FluxRead", "sessions");
}
