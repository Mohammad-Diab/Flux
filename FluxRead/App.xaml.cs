using System.Windows;
using Flux.Ui.Services;
using Flux.Ui.ViewModels;
using Flux.Ui.Views;
using FluxCore.Compression;
using FluxRead.Services;
using FluxRead.ViewModels;
using FluxRead.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FluxRead;

/// <summary>Application entry point: builds the service container and shows the main window.</summary>
public partial class App : Application
{
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        FluxAppHost.ConfigureCommon(services, "FluxRead");
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
        services.AddSingleton(provider => new DecodePipelineService(
            provider.GetRequiredService<CompressionService>(),
            provider.GetRequiredService<ILogger<DecodePipelineService>>()));
        services.AddSingleton<DialogService>();
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
