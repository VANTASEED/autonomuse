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

            // Services
            builder.Services.AddScoped<Autonomuse.Shared.Contracts.ISettingsService, Autonomuse.Services.Orchestration.SettingsService>();

            // Platform-specific services
            builder.Services.AddTransient<Autonomuse.Shared.Contracts.IFolderPicker, Autonomuse.Platforms.Windows.FolderPickerImplementation>();

            // ViewModels
            builder.Services.AddScoped<Autonomuse.ViewModels.StartViewModel>();
            builder.Services.AddScoped<Autonomuse.ViewModels.SettingViewModel>();

#if DEBUG
    		builder.Services.AddBlazorWebViewDeveloperTools();
    		builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
