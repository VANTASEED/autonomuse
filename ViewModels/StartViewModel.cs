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
        private readonly Microsoft.AspNetCore.Components.NavigationManager _navigationManager;
        private string _libraryPath = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string LibraryPath
        {
            get => _libraryPath;
            set
            {
                _libraryPath = value;
                OnPropertyChanged();
            }
        }

        public StartViewModel(ISettingsService settingsService, IFolderPicker folderPicker, Microsoft.AspNetCore.Components.NavigationManager navigationManager)
        {
            _settingsService = settingsService;
            _folderPicker = folderPicker;
            _navigationManager = navigationManager;
            
            _ = InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            LibraryPath = await _settingsService.GetSettingAsync("LibraryPath") ?? string.Empty;
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
