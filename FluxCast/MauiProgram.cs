using Microsoft.Extensions.Logging;
using FluxCast.ViewModels;
using FluxCast.Views;
using FluxCast.Services;
using FluxCore.Compression;
using SkiaSharp.Views.Maui.Controls.Hosting;

namespace FluxCast
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

            // Register services
            builder.Services.AddSingleton<IFolderPicker, FolderPicker>();
            builder.Services.AddSingleton<CompressionService>();
            builder.Services.AddSingleton<InputValidationService>();
            builder.Services.AddSingleton<MetadataFileService>();
            builder.Services.AddSingleton<SessionManager>();

            // Register ViewModels
            builder.Services.AddSingleton<MainViewModel>();
            builder.Services.AddTransient<NewStreamViewModel>();

            // Register Pages
            builder.Services.AddSingleton<MainPage>();
            builder.Services.AddTransient<NewStreamPage>();

#if DEBUG
    		builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
