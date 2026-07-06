using System.IO;
using System.Windows;
using Flux.Ui.Controls;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Flux.Ui.Services;

/// <summary>Startup/shutdown scaffold shared by both apps.</summary>
public static class FluxAppHost
{
    /// <summary>Rolling file logger plus the settings/theming registrations every Flux app needs.</summary>
    public static void ConfigureCommon(ServiceCollection services, string appName)
    {
        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Flux", "logs");
        Log.Logger = new LoggerConfiguration()
            .WriteTo.File(Path.Combine(logDirectory, $"{appName.ToLowerInvariant()}-.log"), rollingInterval: RollingInterval.Day)
            .CreateLogger();

        services.AddLogging(builder => builder.AddSerilog(dispose: true));
        services.AddSingleton(_ => new SettingsService(appName));
        services.AddSingleton(provider => provider.GetRequiredService<SettingsService>().Load());
        services.AddSingleton<ThemeService>();
        services.AddSingleton(provider => new WindowsThemeWatcher(
            provider.GetRequiredService<ThemeService>(),
            () => provider.GetRequiredService<FluxSettings>().ThemeMode));
    }

    /// <summary>Applies saved appearance + motion before the main window is created, then shows it.</summary>
    public static void Start(ServiceProvider services, Func<ServiceProvider, Window> createMain)
    {
        var settings = services.GetRequiredService<FluxSettings>();
        MotionSettings.Current.UserEnableAnimations = settings.EnableAnimations;
        services.GetRequiredService<ThemeService>().Apply(settings.ThemeMode);

        var main = createMain(services);
        services.GetRequiredService<WindowsThemeWatcher>().Attach(main);
        main.Show();
    }

    public static void Shutdown(ServiceProvider? services)
    {
        services?.Dispose();
        Log.CloseAndFlush();
    }
}
