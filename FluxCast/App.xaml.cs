using System.Windows;
using Flux.Ui.Services;
using FluxCast.Services;
using FluxCast.ViewModels;
using FluxCore.Compression;
using FluxCore.Transfer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FluxCast;

/// <summary>Application entry point: builds the service container and shows the main window.</summary>
public partial class App : Application
{
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        FluxAppHost.ConfigureCommon(services, "FluxCast");
        ConfigureServices(services);
        _services = services.BuildServiceProvider();

        FluxAppHost.Start(_services, s => s.GetRequiredService<MainWindow>());
    }

    protected override void OnExit(ExitEventArgs e)
    {
        FluxAppHost.Shutdown(_services);
        base.OnExit(e);
    }

    private static void ConfigureServices(ServiceCollection services)
    {
        services.AddSingleton(provider => new CompressionService(
            logger: provider.GetRequiredService<ILogger<CompressionService>>()));
        services.AddSingleton(provider => new FluxEncodeService(
            provider.GetRequiredService<CompressionService>(),
            provider.GetRequiredService<ILogger<FluxEncodeService>>()));
        services.AddSingleton<SourceValidator>();
        services.AddSingleton<DialogService>();
        services.AddSingleton<CastHistoryService>();
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton(provider => new MainWindow
        {
            DataContext = provider.GetRequiredService<ShellViewModel>(),
        });
    }
}
