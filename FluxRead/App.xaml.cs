using System.IO;
using System.Windows;
using Flux.Ui.Controls;
using Flux.Ui.Services;
using Flux.Ui.ViewModels;
using Flux.Ui.Views;
using FluxCore.Compression;
using FluxRead.Services;
using FluxRead.ViewModels;
using FluxRead.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace FluxRead;

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
            .WriteTo.File(Path.Combine(logDirectory, "fluxread-.log"), rollingInterval: RollingInterval.Day)
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
        services.AddSingleton(provider => new DecodePipelineService(
            provider.GetRequiredService<CompressionService>(),
            provider.GetRequiredService<ILogger<DecodePipelineService>>()));
        services.AddSingleton<DialogService>();
        services.AddSingleton(_ => new SettingsService("FluxRead"));
        services.AddSingleton(provider => provider.GetRequiredService<SettingsService>().Load());
        services.AddSingleton<ThemeService>();
        services.AddSingleton(provider => new WindowsThemeWatcher(
            provider.GetRequiredService<ThemeService>(),
            () => provider.GetRequiredService<FluxSettings>().ThemeMode));
        services.AddSingleton(provider => new SettingsViewModel(
            provider.GetRequiredService<SettingsService>(),
            provider.GetRequiredService<ThemeService>(),
            provider.GetRequiredService<FluxSettings>(),
            onOpenDevTools: () => new InteropDevWindow { Owner = Application.Current.MainWindow }.Show()));
        services.AddSingleton(provider => new SettingsView
        {
            DataContext = provider.GetRequiredService<SettingsViewModel>(),
        });
        services.AddSingleton<FolderDecodeViewModel>();
        services.AddSingleton(provider => new FolderDecodeView
        {
            DataContext = provider.GetRequiredService<FolderDecodeViewModel>(),
        });
        services.AddSingleton(provider => new LiveCaptureView(
            provider.GetRequiredService<DecodePipelineService>(),
            provider.GetRequiredService<DialogService>()));
        services.AddSingleton(provider => new MainWindow(
            provider.GetRequiredService<FolderDecodeView>(),
            provider.GetRequiredService<LiveCaptureView>(),
            provider.GetRequiredService<SettingsView>()));
    }
}
