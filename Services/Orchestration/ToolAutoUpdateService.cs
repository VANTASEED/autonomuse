using System.Diagnostics;
using System.Globalization;
using Autonomuse.Shared.Contracts;
using Microsoft.Extensions.Logging;

namespace Autonomuse.Services.Orchestration
{
    /// <summary>
    /// Keeps the external tools (yt-dlp, Chromaprint, FFmpeg) up to date via winget.
    /// Runs at most once per <see cref="CheckInterval"/> and can be disabled with the AutoUpdateTools setting.
    /// </summary>
    public class ToolAutoUpdateService
    {
        public const string AutoUpdateSetting = "AutoUpdateTools";
        public const string LastCheckSetting = "LastToolUpdateCheck";

        private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);
        private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(10);

        // Processes that lock the tool's executable while it runs
        private static readonly Dictionary<string, string> ProcessNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ["yt-dlp"] = "yt-dlp",
            ["fpcalc"] = "fpcalc",
            ["ffmpeg"] = "ffmpeg"
        };

        private readonly ISettingsService _settingsService;
        private readonly IExternalToolService _toolService;
        private readonly ILogger<ToolAutoUpdateService> _logger;
        private int _running;

        public ToolAutoUpdateService(ISettingsService settingsService, IExternalToolService toolService, ILogger<ToolAutoUpdateService> logger)
        {
            _settingsService = settingsService;
            _toolService = toolService;
            _logger = logger;
        }

        public async Task RunIfDueAsync()
        {
            if (Interlocked.Exchange(ref _running, 1) == 1) return;

            try
            {
                await Task.Delay(StartupDelay);

                var enabled = await _settingsService.GetSettingAsync(AutoUpdateSetting);
                if (string.Equals(enabled, "false", StringComparison.OrdinalIgnoreCase)) return;

                if (!_toolService.HasInternetConnection()) return;

                var lastCheck = await _settingsService.GetSettingAsync(LastCheckSetting);
                if (DateTime.TryParse(lastCheck, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var last)
                    && DateTime.UtcNow - last.ToUniversalTime() < CheckInterval)
                {
                    return;
                }

                var outdated = await _toolService.CheckOutdatedToolsAsync();
                var allHandled = true;

                foreach (var tool in outdated)
                {
                    if (IsToolRunning(tool))
                    {
                        _logger.LogInformation("Skipping auto-update of {Tool}: it is currently running", tool);
                        allHandled = false;
                        continue;
                    }

                    _logger.LogInformation("Auto-updating {Tool}", tool);
                    if (await _toolService.UpgradeToolAsync(tool))
                    {
                        await _toolService.IsToolInstalledAsync(tool); // refreshes the stored version
                    }
                    else
                    {
                        _logger.LogWarning("Auto-update of {Tool} failed", tool);
                        allHandled = false;
                    }
                }

                // Only throttle when everything succeeded so skipped/failed tools retry next launch
                if (allHandled)
                {
                    await _settingsService.SaveSettingAsync(LastCheckSetting, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Automatic tool update failed");
            }
            finally
            {
                Interlocked.Exchange(ref _running, 0);
            }
        }

        private static bool IsToolRunning(string tool)
        {
            if (!ProcessNames.TryGetValue(tool, out var name)) return false;

            var processes = Process.GetProcessesByName(name);
            try
            {
                return processes.Length > 0;
            }
            finally
            {
                foreach (var p in processes) p.Dispose();
            }
        }
    }
}
