using FluxCore.Compression;
using FluxRead.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace FluxRead.WinUI;

public partial class App : Application
{
    private Window? _window;

    public static IServiceProvider Services { get; private set; } = null!;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_ => new CompressionService());
        services.AddSingleton(p => new DecodePipelineService(p.GetRequiredService<CompressionService>()));
        services.AddSingleton<ViewModels.FolderDecodeViewModel>();
        Services = services.BuildServiceProvider();

        _window = new MainWindow();
        _window.Activate();
    }
}
