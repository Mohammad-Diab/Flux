using System.IO;
using System.Windows;
using FluxCore.Compression;
using FluxRead.Services;
using FluxRead.ViewModels;
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

        _services.GetRequiredService<MainWindow>().Show();
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
        services.AddSingleton<FolderDecodeViewModel>();
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton(provider => new MainWindow
        {
            DataContext = provider.GetRequiredService<ShellViewModel>(),
        });
    }
}
