using System.Reflection;

namespace Flux.Ui;

/// <summary>The running app's display version, for the title bar chip.</summary>
public static class AppVersion
{
    /// <summary>e.g. "0.10.0-beta"; empty when the assembly carries no version.</summary>
    public static string Current { get; } = Resolve();

    // The entry assembly is the running app (FluxCast/FluxRead); prefer its informational version,
    // dropping any "+build" suffix, and fall back to the numeric version.
    private static string Resolve()
    {
        var assembly = Assembly.GetEntryAssembly();
        var version = assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrEmpty(version))
            version = assembly?.GetName().Version?.ToString();
        if (string.IsNullOrEmpty(version))
            return "";

        int plus = version.IndexOf('+');
        return plus >= 0 ? version[..plus] : version;
    }
}
