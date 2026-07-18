using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Autonomuse.Shared.Contracts;
using Autonomuse.Shared.DTOs;
using Autonomuse.Services;
using Autonomuse.Services.Orchestration;
using Autonomuse.Infrastructure.Data;
using Autonomuse.Components;
using System.IO;

namespace Autonomuse.ViewModels
{
    public class SettingViewModel : INotifyPropertyChanged
    {
        private readonly ISettingsService _settingsService;
        private readonly IFolderPicker _folderPicker;
        private readonly ColorThemeService _colorService;
        private readonly IHomeUIService _uiService;
        private readonly SqliteDatabaseService _dbService;
        private readonly MediaDatabaseService _mediaDbService;
        private readonly IExternalToolService _toolService;
        private string _currentPath = string.Empty;
        private string _currentAccentColor = "#e8a30e";
        private bool _isAccentTextWhite = false;
        private ObservableCollection<PathHistoryItem> _pathHistory = new();
        private HomeUISettings _uiSettings = new();
        private Dictionary<string, string> _tabThumbnails = new();
        private string _sterilizationInput = string.Empty;
        private ObservableCollection<string> _sterilizationTags = new();
        private string _coverArtQuality = "standard";
        private string _acoustIdApiKey = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        private string _ytDlpVersion = "Unknown";
        private string _chromaprintVersion = "Unknown";
        private string _ffmpegVersion = "Unknown";
        private bool _isYtDlpReady;
        private bool _isFpCalcReady;
        private bool _isFfmpegReady;
        private bool _isUpdatingTools;
        private string _toolStatusMessage = string.Empty;

        public string YtDlpVersion { get => _ytDlpVersion; set { _ytDlpVersion = value; OnPropertyChanged(); } }
        public string ChromaprintVersion { get => _chromaprintVersion; set { _chromaprintVersion = value; OnPropertyChanged(); } }
        public string FfmpegVersion { get => _ffmpegVersion; set { _ffmpegVersion = value; OnPropertyChanged(); } }
        
        public bool IsYtDlpReady { get => _isYtDlpReady; set { _isYtDlpReady = value; OnPropertyChanged(); } }
        public bool IsFpCalcReady { get => _isFpCalcReady; set { _isFpCalcReady = value; OnPropertyChanged(); } }
        public bool IsFfmpegReady { get => _isFfmpegReady; set { _isFfmpegReady = value; OnPropertyChanged(); } }
        
        public bool IsUpdatingTools { get => _isUpdatingTools; set { _isUpdatingTools = value; OnPropertyChanged(); } }
        public string ToolStatusMessage { get => _toolStatusMessage; set { _toolStatusMessage = value; OnPropertyChanged(); } }

        private string _updatingToolName = string.Empty;
        private bool _isYtDlpOutdated;
        private bool _isFpCalcOutdated;
        private bool _isFfmpegOutdated;

        public string UpdatingToolName { get => _updatingToolName; set { _updatingToolName = value; OnPropertyChanged(); } }
        public bool IsYtDlpOutdated { get => _isYtDlpOutdated; set { _isYtDlpOutdated = value; OnPropertyChanged(); } }
        public bool IsFpCalcOutdated { get => _isFpCalcOutdated; set { _isFpCalcOutdated = value; OnPropertyChanged(); } }
        public bool IsFfmpegOutdated { get => _isFfmpegOutdated; set { _isFfmpegOutdated = value; OnPropertyChanged(); } }

        private string _checkingToolName = string.Empty;
        public string CheckingToolName { get => _checkingToolName; set { _checkingToolName = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsCheckingForUpdates)); } }
        public bool IsCheckingForUpdates => !string.IsNullOrEmpty(CheckingToolName);

        public string CurrentAppVersion => UpdateService.CurrentAppVersion;
        private bool _isCheckingAppUpdate;
        public bool IsCheckingAppUpdate { get => _isCheckingAppUpdate; set { _isCheckingAppUpdate = value; OnPropertyChanged(); } }

        public HomeUISettings UiSettings
        {
            get => _uiSettings;
            set { _uiSettings = value; OnPropertyChanged(); }
        }

        public Dictionary<string, string> TabThumbnails
        {
            get => _tabThumbnails;
            set { _tabThumbnails = value; OnPropertyChanged(); }
        }

        public string SterilizationInput
        {
            get => _sterilizationInput;
            set { _sterilizationInput = value; OnPropertyChanged(); }
        }

        public ObservableCollection<string> SterilizationTags
        {
            get => _sterilizationTags;
            set { _sterilizationTags = value; OnPropertyChanged(); }
        }

        public string CoverArtQuality
        {
            get => _coverArtQuality;
            set
            {
                if (_coverArtQuality != value)
                {
                    _coverArtQuality = value;
                    OnPropertyChanged();
                    _ = _settingsService.SaveSettingAsync("CoverArtQuality", value);
                }
            }
        }

        public string AcoustIdApiKey
        {
            get => _acoustIdApiKey;
            set
            {
                if (_acoustIdApiKey != value)
                {
                    _acoustIdApiKey = value;
                    OnPropertyChanged();
                    _ = _settingsService.SaveSettingAsync("AcoustIdApiKey", value);
                }
            }
        }

        public string CurrentPath
        {
            get => _currentPath;
            set { _currentPath = value; OnPropertyChanged(); }
        }

        public string CurrentAccentColor
        {
            get => _currentAccentColor;
            set 
            { 
                if (_currentAccentColor != value)
                {
                    _currentAccentColor = value; 
                    OnPropertyChanged(); 
                    _ = UpdateAccentColorAsync(value, IsAccentTextWhite);
                }
            }
        }

        public bool IsAccentTextWhite
        {
            get => _isAccentTextWhite;
            set 
            {
                if (_isAccentTextWhite != value)
                {
                    _isAccentTextWhite = value;
                    OnPropertyChanged();
                    _ = UpdateAccentColorAsync(CurrentAccentColor, value);
                }
            }
        }

        public bool IsDebugMode => _settingsService.IsDebugMode;

        public ObservableCollection<PathHistoryItem> PathHistory
        {
            get => _pathHistory;
            set { _pathHistory = value; OnPropertyChanged(); }
        }

        public SettingViewModel(ISettingsService settingsService, IFolderPicker folderPicker, ColorThemeService colorService, IHomeUIService uiService, SqliteDatabaseService dbService, MediaDatabaseService mediaDbService, IExternalToolService toolService)
        {
            _settingsService = settingsService;
            _folderPicker = folderPicker;
            _colorService = colorService;
            _uiService = uiService;
            _dbService = dbService;
            _mediaDbService = mediaDbService;
            _toolService = toolService;
            
            _ = InitializeAsync();
        }

        public async Task InitializeAsync()
        {
            CurrentPath = await _settingsService.GetSettingAsync("LibraryPath") ?? string.Empty;
            
            var accentJson = await _settingsService.GetSettingAsync("UserAccents");
            if (!string.IsNullOrEmpty(accentJson))
            {
                var theme = _colorService.DeserializeTheme(accentJson);
                if (theme != null) 
                {
                    _currentAccentColor = theme.Base;
                    _isAccentTextWhite = theme.ContrastText.Equals("#FFFFFF", StringComparison.OrdinalIgnoreCase);
                }
            }
            else
            {
                // Revert to factory defaults if record is missing (Reset case)
                _currentAccentColor = "#e8a30e";
                _isAccentTextWhite = false;
            }

            // Ensure UI is notified of these specific changes
            OnPropertyChanged(nameof(CurrentAccentColor));
            OnPropertyChanged(nameof(IsAccentTextWhite));

            await RefreshHistoryAsync();
            UiSettings = await _uiService.GetSettingsAsync();
            
            await RefreshThumbnailsAsync();

            var sterilizationStr = await _settingsService.GetSettingAsync("StringSterilization");
            if (!string.IsNullOrEmpty(sterilizationStr))
            {
                var tags = sterilizationStr.Split(',', StringSplitOptions.RemoveEmptyEntries);
                SterilizationTags = new ObservableCollection<string>(tags);
            }

            var quality = await _settingsService.GetSettingAsync("CoverArtQuality");
            _coverArtQuality = quality ?? "standard";
            OnPropertyChanged(nameof(CoverArtQuality));

            _acoustIdApiKey = await _settingsService.GetSettingAsync("AcoustIdApiKey") ?? string.Empty;
            OnPropertyChanged(nameof(AcoustIdApiKey));

            await RefreshToolsInfoAsync();
            _ = Task.Run(CheckForUpdatesAsync);
        }

        public async Task RefreshToolsInfoAsync()
        {
            IsYtDlpReady = await _toolService.IsToolInstalledAsync("yt-dlp");
            IsFpCalcReady = await _toolService.IsToolInstalledAsync("fpcalc");
            IsFfmpegReady = await _toolService.IsToolInstalledAsync("ffmpeg");

            YtDlpVersion = await _settingsService.GetSettingAsync("YtDlpVersion") ?? "Unknown";
            ChromaprintVersion = await _settingsService.GetSettingAsync("ChromaprintVersion") ?? "Unknown";
            FfmpegVersion = await _settingsService.GetSettingAsync("FfmpegVersion") ?? "Unknown";
        }

        private string GetToolDisplayName(string toolName)
        {
            if (toolName.Equals("fpcalc", StringComparison.OrdinalIgnoreCase)) return "Chromaprint";
            return toolName;
        }

        public async Task UpdateToolAsync(string toolName)
        {
            if (IsUpdatingTools) return;
            UpdatingToolName = toolName;
            IsUpdatingTools = true;
            string displayName = GetToolDisplayName(toolName);
            ToolStatusMessage = $"Updating {displayName}... Please wait.";

            try
            {
                if (!_toolService.HasInternetConnection())
                {
                    ToolStatusMessage = "ERROR: No internet connection.";
                    return;
                }

                await _toolService.InstallToolAsync(toolName);
                await Task.Delay(1000);
                await RefreshToolsInfoAsync();

                // Clear outdated flag on successful update
                if (toolName.Equals("yt-dlp", StringComparison.OrdinalIgnoreCase)) IsYtDlpOutdated = false;
                else if (toolName.Equals("fpcalc", StringComparison.OrdinalIgnoreCase)) IsFpCalcOutdated = false;
                else if (toolName.Equals("ffmpeg", StringComparison.OrdinalIgnoreCase)) IsFfmpegOutdated = false;

                // Double check if installed successfully
                bool success = false;
                if (toolName.Equals("yt-dlp", StringComparison.OrdinalIgnoreCase)) success = IsYtDlpReady;
                else if (toolName.Equals("fpcalc", StringComparison.OrdinalIgnoreCase)) success = IsFpCalcReady;
                else if (toolName.Equals("ffmpeg", StringComparison.OrdinalIgnoreCase)) success = IsFfmpegReady;

                if (success)
                {
                    ToolStatusMessage = $"SUCCESS: {displayName} updated and verified successfully!";
                }
                else
                {
                    ToolStatusMessage = $"WARN: {displayName} installation finished but detection failed. Try restarting the app.";
                }
            }
            catch (Exception ex)
            {
                ToolStatusMessage = $"ERROR: Failed to update {displayName}: {ex.Message}";
            }
            finally
            {
                IsUpdatingTools = false;
                UpdatingToolName = string.Empty;
            }
        }

        public async Task ReCheckToolAsync(string toolName)
        {
            if (IsUpdatingTools || IsCheckingForUpdates) return;
            CheckingToolName = toolName;
            string displayName = GetToolDisplayName(toolName);
            ToolStatusMessage = $"Checking for updates on {displayName}...";

            try
            {
                if (!_toolService.HasInternetConnection())
                {
                    ToolStatusMessage = "ERROR: No internet connection.";
                    return;
                }

                // Refresh tool status and version first
                await RefreshToolsInfoAsync();

                // Check winget upgrade status
                var outdated = await _toolService.CheckOutdatedToolsAsync();
                bool isOutdated = false;
                if (toolName.Equals("yt-dlp", StringComparison.OrdinalIgnoreCase))
                {
                    IsYtDlpOutdated = outdated.Contains("yt-dlp");
                    isOutdated = IsYtDlpOutdated;
                }
                else if (toolName.Equals("fpcalc", StringComparison.OrdinalIgnoreCase))
                {
                    IsFpCalcOutdated = outdated.Contains("fpcalc");
                    isOutdated = IsFpCalcOutdated;
                }
                else if (toolName.Equals("ffmpeg", StringComparison.OrdinalIgnoreCase))
                {
                    IsFfmpegOutdated = outdated.Contains("ffmpeg");
                    isOutdated = IsFfmpegOutdated;
                }

                if (isOutdated)
                {
                    ToolStatusMessage = $"SUCCESS: An update is available for {displayName}!";
                }
                else
                {
                    ToolStatusMessage = $"SUCCESS: {displayName} is already up to date.";
                }
            }
            catch (Exception ex)
            {
                ToolStatusMessage = $"ERROR: Failed to check updates: {ex.Message}";
            }
            finally
            {
                CheckingToolName = string.Empty;
            }
        }

        public async Task CheckForUpdatesAsync()
        {
            if (!_toolService.HasInternetConnection()) return;
            
            try
            {
                var outdated = await _toolService.CheckOutdatedToolsAsync();
                IsYtDlpOutdated = outdated.Contains("yt-dlp");
                IsFpCalcOutdated = outdated.Contains("fpcalc");
                IsFfmpegOutdated = outdated.Contains("ffmpeg");
            }
            catch
            {
                // Ignore background errors
            }
        }

        public async Task CheckForAppUpdateAsync()
        {
            if (IsCheckingAppUpdate) return;
            IsCheckingAppUpdate = true;
            ToolStatusMessage = string.Empty;
            try
            {
                await UpdateService.CheckForUpdateAsync(isManual: true);
            }
            catch (Exception ex)
            {
                ToolStatusMessage = $"ERROR: {ex.Message}";
            }
            finally
            {
                IsCheckingAppUpdate = false;
            }
        }

        public async Task RefreshThumbnailsAsync()
        {
            var tabs = new[] { "Dashboard", "Audio", "Video", "Settings" };
            var thumbs = new Dictionary<string, string>();
            foreach (var tab in tabs)
            {
                thumbs[tab] = await _uiService.GetThumbnailAsBase64Async(tab);
            }
            TabThumbnails = thumbs;
        }

        private async Task RefreshHistoryAsync()
        {
            var history = await _settingsService.GetPathHistoryAsync();
            var items = history.Select(path => new PathHistoryItem 
            { 
                Path = path, 
                Exists = Directory.Exists(path) 
            }).ToList();
            
            PathHistory = new ObservableCollection<PathHistoryItem>(items);
        }

        public async Task UpdatePathAsync()
        {
            var oldPath = CurrentPath;
            var newPath = await _folderPicker.PickFolderAsync();

            if (!string.IsNullOrEmpty(newPath) && newPath != oldPath)
            {
                // Immediate validation
                if (!Directory.Exists(newPath))
                {
                    if (App.Current?.Windows.Count > 0 && App.Current.Windows[0].Page is Page page)
                    {
                        await page.DisplayAlertAsync("Invalid Path", 
                            $"The selected folder '{newPath}' does not exist or is inaccessible.", "OK");
                    }
                    return;
                }

                await ApplyNewPathAsync(oldPath, newPath);
            }
        }

        public async Task SwitchToPathAsync(PathHistoryItem item)
        {
            var oldPath = CurrentPath;
            var newPath = item.Path;

            if (newPath == oldPath) return;

            // Re-check existence immediately on selection
            if (!Directory.Exists(newPath))
            {
                bool create = false;
                if (App.Current?.Windows.Count > 0 && App.Current.Windows[0].Page is Page page)
                {
                    create = await page.DisplayAlertAsync("Folder Missing", 
                        $"The folder '{newPath}' no longer exists. Would you like to recreate it?", "Yes", "No");
                }
                
                if (create)
                {
                    await RecreatePathAsync(item);
                }
                else
                {
                    // Refresh history to show the warning icon correctly
                    await RefreshHistoryAsync();
                }
                return;
            }

            await ApplyNewPathAsync(oldPath, newPath);
        }

        public async Task RecreatePathAsync(PathHistoryItem item)
        {
            try
            {
                if (!Directory.Exists(item.Path))
                {
                    Directory.CreateDirectory(item.Path);
                }
                
                await ApplyNewPathAsync(CurrentPath, item.Path);
            }
            catch (Exception ex)
            {
                if (App.Current?.Windows.Count > 0 && App.Current.Windows[0].Page is Page page)
                {
                    await page.DisplayAlertAsync("Error", $"Could not recreate folder: {ex.Message}", "OK");
                }
            }
        }

        private async Task ApplyNewPathAsync(string oldPath, string newPath)
        {
            await _settingsService.RemovePathHistoryAsync(newPath);

            // 1. Record the OLD path to history
            if (!string.IsNullOrEmpty(oldPath))
            {
                await _settingsService.AddPathHistoryAsync(oldPath);
            }

            // 2. Update to new path
            CurrentPath = newPath;
            await _settingsService.SaveSettingAsync("LibraryPath", newPath);

            // 3. Refresh display
            await RefreshHistoryAsync();
        }

        public void OpenInExplorer(string path)
        {
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                // This is a Windows-only MAUI app as per previous project restriction
                Process.Start("explorer.exe", path);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to open explorer: {ex.Message}");
            }
        }

        private async Task UpdateAccentColorAsync(string hex, bool isTextWhite)
        {
            var theme = _colorService.GenerateTheme(hex, isTextWhite);
            var json = _colorService.SerializeTheme(theme);
            await _settingsService.SaveSettingAsync("UserAccents", json);
            
            Autonomuse.Components.AccentManager.NotifyThemeChanged();
        }

        public async Task PickBackgroundAsync(string tabName)
        {
            try
            {
                var result = await FilePicker.Default.PickAsync(new PickOptions
                {
                    PickerTitle = $"Select Background for {tabName}",
                    FileTypes = FilePickerFileType.Images
                });

                if (result != null)
                {
                    await _uiService.SaveBackgroundAsync(tabName, result.FullPath);
                    await RefreshThumbnailsAsync();
                    OnPropertyChanged(nameof(UiSettings));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Background selection failed: {ex.Message}");
            }
        }

        public async Task RemoveBackgroundAsync(string tabName)
        {
            await _uiService.RemoveBackgroundAsync(tabName);
            await RefreshThumbnailsAsync();
            OnPropertyChanged(nameof(UiSettings));
        }

        public async Task UpdateUiSettingsAsync()
        {
            await _uiService.SaveSettingsAsync(UiSettings);
        }

        public async Task AddSterilizationTagAsync(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return;
            
            var cleanTag = tag.Trim().Trim(',');
            if (!string.IsNullOrEmpty(cleanTag) && !_sterilizationTags.Contains(cleanTag))
            {
                _sterilizationTags.Add(cleanTag);
                await SaveSterilizationSettingsAsync();
            }
            SterilizationInput = string.Empty;
        }

        public async Task RemoveSterilizationTagAsync(string tag)
        {
            if (_sterilizationTags.Contains(tag))
            {
                _sterilizationTags.Remove(tag);
                await SaveSterilizationSettingsAsync();
            }
        }

        private async Task SaveSterilizationSettingsAsync()
        {
            var val = string.Join(",", _sterilizationTags);
            await _settingsService.SaveSettingAsync("StringSterilization", val);
        }

        public async Task ClearAllUploadedDataAsync()
        {
            try
            {
                // Clear all records via DB Service
                await _mediaDbService.ClearAllDataAsync();

                // Delete physical files and folders in LibraryPath
                if (!string.IsNullOrEmpty(CurrentPath) && Directory.Exists(CurrentPath))
                {
                    try 
                    {
                        foreach (var dir in Directory.GetDirectories(CurrentPath))
                        {
                            Directory.Delete(dir, true);
                        }
                        foreach (var file in Directory.GetFiles(CurrentPath))
                        {
                            File.Delete(file);
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        public async Task ResetPersonalizationAsync()
        {
            try
            {
                // 1. Reset via DB Service (HomeUISettings, UserAccents, Background_*)
                await _dbService.ResetPersonalizationAsync();
                
                // 2. Reset Background Files via HomeUIService
                await _uiService.ResetAllBackgroundsAsync();

                // 3. Re-initialize ViewModel (reloads default settings)
                await InitializeAsync();

                // 4. Trigger UI Updates
                AccentManager.NotifyThemeChanged();
                OnPropertyChanged(nameof(UiSettings));
            }
            catch { }
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
