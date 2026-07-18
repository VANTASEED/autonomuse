using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Autonomuse.Shared.Contracts;

namespace Autonomuse.ViewModels
{
    public class StartViewModel : INotifyPropertyChanged
    {
        private readonly ISettingsService _settingsService;
        private readonly IFolderPicker _folderPicker;
        private readonly IExternalToolService _toolService;
        private readonly Microsoft.AspNetCore.Components.NavigationManager _navigationManager;
        private string _libraryPath = string.Empty;
        private bool _isYtDlpReady = false;
        private bool _isFpCalcReady = false;
        private bool _isFfmpegReady = false;
        private bool _isDownloading = false;
        private bool _isInitializing = true;
        private double _downloadProgress = 0;
        private string _statusMessage = "Ready to go";

        public event PropertyChangedEventHandler? PropertyChanged;

        public bool IsInitializing
        {
            get => _isInitializing;
            set { _isInitializing = value; OnPropertyChanged(); }
        }

        public string LibraryPath
        {
            get => _libraryPath;
            set { _libraryPath = value; OnPropertyChanged(); }
        }

        public bool IsYtDlpReady
        {
            get => _isYtDlpReady;
            set { _isYtDlpReady = value; OnPropertyChanged(); }
        }

        public bool IsFpCalcReady
        {
            get => _isFpCalcReady;
            set { _isFpCalcReady = value; OnPropertyChanged(); }
        }

        public bool IsFfmpegReady
        {
            get => _isFfmpegReady;
            set { _isFfmpegReady = value; OnPropertyChanged(); }
        }

        public bool IsDownloading
        {
            get => _isDownloading;
            set { _isDownloading = value; OnPropertyChanged(); }
        }

        public double DownloadProgress
        {
            get => _downloadProgress;
            set { _downloadProgress = value; OnPropertyChanged(); }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        public StartViewModel(ISettingsService settingsService, IFolderPicker folderPicker, IExternalToolService toolService, Microsoft.AspNetCore.Components.NavigationManager navigationManager)
        {
            _settingsService = settingsService;
            _folderPicker = folderPicker;
            _toolService = toolService;
            _navigationManager = navigationManager;
            
            _ = InitializeAsync();
        }

        public async Task InitializeAsync()
        {
            IsInitializing = true;
            await _settingsService.InitializeCoreSettingsAsync();
            LibraryPath = await _settingsService.GetSettingAsync("LibraryPath") ?? string.Empty;
            await CheckToolsStatusAsync();
        }

        public async Task CheckToolsStatusAsync()
        {
            IsYtDlpReady = await _toolService.IsToolInstalledAsync("yt-dlp");
            IsFpCalcReady = await _toolService.IsToolInstalledAsync("fpcalc");
            IsFfmpegReady = await _toolService.IsToolInstalledAsync("ffmpeg");
            
            if (!IsYtDlpReady && !IsFpCalcReady && !IsFfmpegReady)
                StatusMessage = "External tools (yt-dlp, fpcalc, ffmpeg) are missing.";
            else if (!IsYtDlpReady)
                StatusMessage = "YouTube Downloader (yt-dlp) is missing.";
            else if (!IsFpCalcReady)
                StatusMessage = "Audio Fingerprinter (fpcalc) is missing.";
            else if (!IsFfmpegReady)
                StatusMessage = "FFmpeg is missing.";
            else
                StatusMessage = "External tools are ready.";
        }

        public async Task InstallYtDlpAsync() => await InstallToolAsync("yt-dlp");
        public async Task InstallFpCalcAsync() => await InstallToolAsync("fpcalc");
        public async Task InstallFfmpegAsync() => await InstallToolAsync("ffmpeg");

        private async Task InstallToolAsync(string toolName)
        {
            if (!_toolService.HasInternetConnection())
            {
                StatusMessage = "Error: No internet connection.";
                return;
            }

            try
            {
                IsDownloading = true;
                StatusMessage = $"Installing {toolName} via winget. This may take a minute...";
                
                await _toolService.InstallToolAsync(toolName);
                
                await Task.Delay(1000); 
                if (toolName == "yt-dlp") IsYtDlpReady = await _toolService.IsToolInstalledAsync("yt-dlp");
                else if (toolName == "ffmpeg") IsFfmpegReady = await _toolService.IsToolInstalledAsync("ffmpeg");
                else IsFpCalcReady = await _toolService.IsToolInstalledAsync("fpcalc");
                
                if (await _toolService.IsToolInstalledAsync(toolName))
                {
                    StatusMessage = $"{toolName} installed and verified!";
                }
                else
                {
                    StatusMessage = $"winget finished but {toolName} not detected. You may need to restart the app.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Installation failed: {ex.Message}";
            }
            finally
            {
                IsDownloading = false;
            }
        }

        public async Task CheckOnBoardingStatusAsync()
        {
            var libraryPath = await _settingsService.GetSettingAsync("LibraryPath");
            var onboarding = await _settingsService.GetSettingAsync("OnBoarding");

            if (!string.IsNullOrEmpty(libraryPath) && onboarding == "1")
            {
                _navigationManager.NavigateTo("home");
            }
        }

        public async Task SelectLibraryFolderAsync()
        {
            var path = await _folderPicker.PickFolderAsync();
            if (!string.IsNullOrEmpty(path))
            {
                LibraryPath = path;
                await _settingsService.SaveSettingAsync("LibraryPath", path);
            }
        }

        public async Task CompleteOnBoardingAsync()
        {
            if (!string.IsNullOrEmpty(LibraryPath))
            {
                await _settingsService.SaveSettingAsync("OnBoarding", "1");
                _navigationManager.NavigateTo("home");
            }
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
