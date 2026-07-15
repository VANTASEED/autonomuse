using Microsoft.Extensions.Logging;

namespace Autonomuse
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                });

            builder.Services.AddMauiBlazorWebView();

            // Infrastructure & Data
            builder.Services.AddSingleton<Autonomuse.Infrastructure.Data.SqliteDatabaseService>();
            builder.Services.AddSingleton<Autonomuse.Infrastructure.Data.MediaDatabaseService>();

            // External APIs
            builder.Services.AddHttpClient();

            // Audio Watcher
            builder.Services.AddSingleton<Autonomuse.Services.Orchestration.AudioWatchService>();

            // Video Watcher
            builder.Services.AddSingleton<Autonomuse.Services.Orchestration.VideoWatchService>();

            // Services
            builder.Services.AddSingleton<Autonomuse.Shared.Contracts.ISettingsService, Autonomuse.Services.Orchestration.SettingsService>();
            builder.Services.AddScoped<Autonomuse.Shared.Contracts.IAudioService, Autonomuse.Services.Orchestration.AudioService>();
            builder.Services.AddScoped<Autonomuse.Shared.Contracts.IAudioEnrichmentService, Autonomuse.Services.Orchestration.AudioEnrichmentService>();
            builder.Services.AddScoped<Autonomuse.Shared.Contracts.IVideoService, Autonomuse.Services.Orchestration.VideoService>();
            builder.Services.AddScoped<Autonomuse.Shared.Contracts.IYoutubeService, Autonomuse.Services.Orchestration.YoutubeService>();
            builder.Services.AddSingleton<Autonomuse.Services.Orchestration.ColorThemeService>();
            builder.Services.AddSingleton<Autonomuse.Shared.Contracts.IHomeUIService, Autonomuse.Services.Orchestration.HomeUIService>();

            // Platform-specific services
            builder.Services.AddTransient<Autonomuse.Shared.Contracts.IFolderPicker, Autonomuse.Platforms.Windows.FolderPickerImplementation>();
            builder.Services.AddSingleton<Autonomuse.Shared.Contracts.IExternalToolService, Autonomuse.Services.Orchestration.ExternalToolService>();

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

