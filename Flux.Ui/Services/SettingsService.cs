using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Flux.Ui.Services;

/// <summary>Appearance preference: follow Windows, or force light/dark.</summary>
public enum AppThemeMode { System, Light, Dark }

/// <summary>Persisted user preferences.</summary>
public sealed class FluxSettings
{
    public AppThemeMode ThemeMode { get; set; } = AppThemeMode.System;
    public bool PerformanceMode { get; set; }
}

/// <summary>Loads and saves <see cref="FluxSettings"/> as JSON under %LOCALAPPDATA%\Flux\{appName}.</summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;

    /// <param name="appName">Per-app folder name (e.g. "FluxCast", "FluxRead").</param>
    public SettingsService(string appName)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Flux", appName);
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "settings.json");
    }

    /// <summary>Reads settings, returning defaults on a missing or unreadable file.</summary>
    public FluxSettings Load()
    {
        try
        {
            if (File.Exists(_path))
                return JsonSerializer.Deserialize<FluxSettings>(File.ReadAllText(_path), Options) ?? new FluxSettings();
        }
        catch
        {
            // Corrupt or unreadable settings fall back to defaults.
        }

        return new FluxSettings();
    }

    /// <summary>Writes settings; a failed write is swallowed rather than crashing the app.</summary>
    public void Save(FluxSettings settings)
    {
        try { File.WriteAllText(_path, JsonSerializer.Serialize(settings, Options)); }
        catch { /* best-effort persistence */ }
    }
}
