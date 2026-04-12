using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Autonomuse.Shared.Contracts;

namespace Autonomuse.ViewModels
{
    public class SettingViewModel : INotifyPropertyChanged
    {
        private readonly ISettingsService _settingsService;
        private readonly IFolderPicker _folderPicker;
        private string _currentPath = string.Empty;
        private ObservableCollection<PathHistoryItem> _pathHistory = new();

        public event PropertyChangedEventHandler? PropertyChanged;

        public string CurrentPath
        {
            get => _currentPath;
            set { _currentPath = value; OnPropertyChanged(); }
        }

        public ObservableCollection<PathHistoryItem> PathHistory
        {
            get => _pathHistory;
            set { _pathHistory = value; OnPropertyChanged(); }
        }

        public SettingViewModel(ISettingsService settingsService, IFolderPicker folderPicker)
        {
            _settingsService = settingsService;
            _folderPicker = folderPicker;

            _ = InitializeAsync();
        }

        public async Task InitializeAsync()
        {
            CurrentPath = await _settingsService.GetSettingAsync("LibraryPath") ?? string.Empty;
            await RefreshHistoryAsync();
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
                    await App.Current.MainPage.DisplayAlert("Invalid Path", 
                        $"The selected folder '{newPath}' does not exist or is inaccessible.", "OK");
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
                bool create = await App.Current.MainPage.DisplayAlert("Folder Missing", 
                    $"The folder '{newPath}' no longer exists. Would you like to recreate it?", "Yes", "No");
                
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
                await App.Current.MainPage.DisplayAlert("Error", $"Could not recreate folder: {ex.Message}", "OK");
            }
        }

        private async Task ApplyNewPathAsync(string oldPath, string newPath)
        {
            // History Safety: if the new path was in history, remove it (it is now current)
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

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class PathHistoryItem
    {
        public string Path { get; set; } = string.Empty;
        public bool Exists { get; set; }
    }
}
