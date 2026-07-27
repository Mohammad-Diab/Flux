using Flux.Ui.Services;
using Flux.Ui.WinUI.Services;
using FluxCast.Services;
using FluxCore.Compression;
using FluxCore.Transfer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;

namespace FluxCast.WinUI;

public partial class App : Application
{
    private MainWindow? _window;

    public static IServiceProvider Services { get; private set; } = null!;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var settings = new SettingsService("FluxCast");
        var model = settings.Load();

        var services = new ServiceCollection();
        services.AddSingleton(_ => new CompressionService());
        services.AddSingleton(p => new FluxEncodeService(p.GetRequiredService<CompressionService>()));
        services.AddSingleton(_ => new SourceValidator());
        services.AddSingleton(_ => new CastHistoryService());
        services.AddSingleton<DialogService>();
        services.AddSingleton(settings);
        services.AddSingleton(model);
        // No sinks yet: the WPF app's Serilog file logging is a cutover concern, not a port one.
        services.AddSingleton<ILoggerFactory>(_ => LoggerFactory.Create(_ => { }));
        services.AddSingleton(_ => new Flux.Ui.WinUI.ViewModels.SettingsViewModel(
            settings, model, mode => _window?.ApplyTheme(mode)));
        Services = services.BuildServiceProvider();

        _window = new MainWindow();
        _window.ApplyTheme(model.ThemeMode);
        _window.Activate();
    }
}
