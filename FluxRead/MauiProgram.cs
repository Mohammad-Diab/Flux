using Microsoft.Extensions.Logging;
using FluxRead.ViewModels;
using FluxRead.Views;
using FluxRead.Services;
using SkiaSharp.Views.Maui.Controls.Hosting;

namespace FluxRead
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .UseSkiaSharp()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

            // Register Services
            builder.Services.AddSingleton<ScreenCaptureService>();
            builder.Services.AddSingleton<WindowPositioningService>();
            builder.Services.AddSingleton<DecoderSessionManager>();

            // Register ViewModels
            builder.Services.AddSingleton<MainViewModel>();
            builder.Services.AddTransient<InputModeViewModel>();

            // Register Pages
            builder.Services.AddSingleton<MainPage>();
            builder.Services.AddTransient<InputModePage>();

#if DEBUG
                builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
