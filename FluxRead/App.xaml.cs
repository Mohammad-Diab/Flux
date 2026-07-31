using Flux.Ui.Services;
using FluxCore.Compression;
using FluxCore.Transfer;
using FluxRead.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace FluxRead;

public partial class App : Application
{
    private MainWindow? _window;

    public static IServiceProvider Services { get; private set; } = null!;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var settings = new SettingsService("FluxRead");
        var model = settings.Load();

        var services = new ServiceCollection();
        services.AddSingleton(_ => new CompressionService());
        services.AddSingleton(p => new DecodePipelineService(p.GetRequiredService<CompressionService>()));
        services.AddSingleton<ViewModels.FolderDecodeViewModel>();
        services.AddSingleton<Flux.Ui.Services.DialogService>();
        services.AddSingleton(_ => new ReceptionHistoryService());
        services.AddSingleton<ViewModels.ReceivedItemsViewModel>();
        services.AddSingleton(settings);
        services.AddSingleton(model);
        services.AddSingleton(_ => new Flux.Ui.ViewModels.SettingsViewModel(
            settings, model, mode => _window?.ApplyTheme(mode)));
        Services = services.BuildServiceProvider();

        _window = new MainWindow();
        _window.ApplyTheme(model.ThemeMode);
        _window.Activate();
    }
}
