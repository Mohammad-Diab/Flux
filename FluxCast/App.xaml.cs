using System.IO;
using System.Windows;
using Flux.Ui.Controls;
using Flux.Ui.Services;
using FluxCast.Services;
using FluxCast.ViewModels;
using FluxCore.Compression;
using FluxCore.Transfer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace FluxCast;

/// <summary>
/// Application entry point: builds the service container and shows the main window.
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _services;

    /// <inheritdoc/>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Flux", "logs");
        Log.Logger = new LoggerConfiguration()
            .WriteTo.File(Path.Combine(logDirectory, "fluxcast-.log"), rollingInterval: RollingInterval.Day)
            .CreateLogger();

        var services = new ServiceCollection();
        ConfigureServices(services);
        _services = services.BuildServiceProvider();

        // Apply saved appearance + motion before any window renders.
        var settings = _services.GetRequiredService<FluxSettings>();
        MotionSettings.Current.UserEnableAnimations = settings.EnableAnimations;
        _services.GetRequiredService<ThemeService>().Apply(settings.ThemeMode);

        var main = _services.GetRequiredService<MainWindow>();
        _services.GetRequiredService<WindowsThemeWatcher>().Attach(main);
        main.Show();
    }

    /// <inheritdoc/>
    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private static void ConfigureServices(ServiceCollection services)
    {
        services.AddLogging(builder => builder.AddSerilog(dispose: true));
        services.AddSingleton(provider => new CompressionService(
            logger: provider.GetRequiredService<ILogger<CompressionService>>()));
        services.AddSingleton(provider => new FluxEncodeService(
            provider.GetRequiredService<CompressionService>(),
            provider.GetRequiredService<ILogger<FluxEncodeService>>()));
        services.AddSingleton<SourceValidator>();
        services.AddSingleton<DialogService>();
        services.AddSingleton(_ => new SettingsService("FluxCast"));
        services.AddSingleton(provider => provider.GetRequiredService<SettingsService>().Load());
        services.AddSingleton<ThemeService>();
        services.AddSingleton(provider => new WindowsThemeWatcher(
            provider.GetRequiredService<ThemeService>(),
            () => provider.GetRequiredService<FluxSettings>().ThemeMode));
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton(provider => new MainWindow
        {
            DataContext = provider.GetRequiredService<ShellViewModel>(),
        });
    }
}
