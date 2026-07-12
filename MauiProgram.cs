using Microsoft.Extensions.Logging;
using Serilog;

namespace Autonomuse
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            // Configure Serilog for centralized logging (per architecture spec: Serilog MANDATORY)
            var logDirectory = Path.Combine(AppContext.BaseDirectory, "buildlogging");
            if (!Directory.Exists(logDirectory))
            {
                Directory.CreateDirectory(logDirectory);
            }

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.File(
                    Path.Combine(logDirectory, "autonomuse-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                });

            builder.Services.AddMauiBlazorWebView();

            // Serilog integration
            builder.Logging.AddSerilog(Log.Logger);

            // Infrastructure & Data
            builder.Services.AddSingleton<Autonomuse.Infrastructure.Data.SqliteDatabaseService>();
            builder.Services.AddSingleton<Autonomuse.Infrastructure.Data.MediaDatabaseService>();

            // External APIs
            builder.Services.AddHttpClient();

            // Services
            builder.Services.AddSingleton<Autonomuse.Shared.Contracts.ISettingsService, Autonomuse.Services.Orchestration.SettingsService>();
            builder.Services.AddScoped<Autonomuse.Shared.Contracts.IAudioService, Autonomuse.Services.Orchestration.AudioService>();
            builder.Services.AddScoped<Autonomuse.Shared.Contracts.IAudioEnrichmentService, Autonomuse.Services.Orchestration.AudioEnrichmentService>();
            builder.Services.AddScoped<Autonomuse.Shared.Contracts.IVideoService, Autonomuse.Services.Orchestration.VideoService>();
            builder.Services.AddScoped<Autonomuse.Shared.Contracts.IImageService, Autonomuse.Services.Orchestration.ImageService>();
            builder.Services.AddScoped<Autonomuse.Shared.Contracts.IEBookService, Autonomuse.Services.Orchestration.EBookService>();
            builder.Services.AddScoped<Autonomuse.Shared.Contracts.IYoutubeService, Autonomuse.Services.Orchestration.YoutubeService>();
            builder.Services.AddSingleton<Autonomuse.Services.Orchestration.ColorThemeService>();
            builder.Services.AddSingleton<Autonomuse.Shared.Contracts.IHomeUIService, Autonomuse.Services.Orchestration.HomeUIService>();

            // Platform-specific services
            builder.Services.AddTransient<Autonomuse.Shared.Contracts.IFolderPicker, Autonomuse.Platforms.Windows.FolderPickerImplementation>();
            builder.Services.AddSingleton<Autonomuse.Shared.Contracts.IExternalToolService, Autonomuse.Services.Orchestration.ExternalToolService>();

            // Resilience (Polly)
            builder.Services.AddSingleton<Autonomuse.Services.Orchestration.ResiliencePipelineService>();

            // ViewModels
            builder.Services.AddScoped<Autonomuse.ViewModels.StartViewModel>();
            builder.Services.AddScoped<Autonomuse.ViewModels.SettingViewModel>();
            builder.Services.AddScoped<Autonomuse.ViewModels.DashboardViewModel>();

#if DEBUG
    		builder.Services.AddBlazorWebViewDeveloperTools();
    		builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}

