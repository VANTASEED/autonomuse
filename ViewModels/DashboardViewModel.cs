using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Autonomuse.Shared.Contracts;
using Autonomuse.Shared.DTOs;
using Autonomuse.Shared.Enums;
using Microsoft.AspNetCore.Components.Forms;
using System.Diagnostics;
using System.IO;
namespace Autonomuse.ViewModels
{


    public class DashboardViewModel : INotifyPropertyChanged
    {
        private readonly IAudioService _audioService;
        private readonly IVideoService _videoService;
        private readonly ISettingsService _settingsService;
        private readonly IYoutubeService _youtubeService;
        private readonly IExternalToolService _toolService;

        private MediaType _selectedMediaType = MediaType.Audio;
        private string? _selectedOrganizationGuid;
        private string _newOrganizationName = string.Empty;
        private string _statusMessage = string.Empty;
        private string _youtubeUrl = string.Empty;
        private bool _isUploading;
        private bool _isYoutubeDownloading;
        private string _youtubeStatusMessage = string.Empty;
        private int _uploadedCount;
        private int _totalCount;
        private ObservableCollection<OrganizationOption> _organizationOptions = new();
        private Guid _inputFileKey = Guid.NewGuid();
        private Guid _statusId = Guid.NewGuid();
        private ObservableCollection<YoutubeDownloadFailure> _youtubeDownloadFailures = new();
        private bool _isYoutubeStatusExpanded;

        public event PropertyChangedEventHandler? PropertyChanged;

        // Supported extensions per media type
        private static readonly Dictionary<MediaType, HashSet<string>> ExtensionMap = new()
        {
            [MediaType.Audio] = new(StringComparer.OrdinalIgnoreCase) { ".mp3", ".aac", ".wav", ".flac" },
            [MediaType.Video] = new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".mkv", ".avi", ".mov", ".webm" },
        };

        #region Properties

        public MediaType SelectedMediaType
        {
            get => _selectedMediaType;
            set 
            { 
                _selectedMediaType = value; 
                OnPropertyChanged(); 
                OnPropertyChanged(nameof(ShowOrganization)); 
                OnPropertyChanged(nameof(ShowDropbox)); 
                OnPropertyChanged(nameof(DropboxIcon)); 
                OnPropertyChanged(nameof(DropboxLabel)); 
                OnPropertyChanged(nameof(OrganizationLabel)); 
                OnPropertyChanged(nameof(AcceptedExtensions));
                OnPropertyChanged(nameof(ShowYoutubeSection));
                OnPropertyChanged(nameof(YoutubeDownloadButtonLabel));

            }
        }

        public string? SelectedOrganizationGuid
        {
            get => _selectedOrganizationGuid;
            set 
            { 
                _selectedOrganizationGuid = value; 
                OnPropertyChanged();

            }
        }

        public ObservableCollection<OrganizationOption> OrganizationOptions
        {
            get => _organizationOptions;
            set { _organizationOptions = value; OnPropertyChanged(); }
        }

        public string NewOrganizationName
        {
            get => _newOrganizationName;
            set { _newOrganizationName = value; OnPropertyChanged(); }
        }

        public Guid InputFileKey
        {
            get => _inputFileKey;
            set { _inputFileKey = value; OnPropertyChanged(); }
        }

        private CancellationTokenSource? _statusCts;
        private CancellationTokenSource? _youtubeStatusCts;

        public string StatusMessage
        {
            get => _statusMessage;
            set 
            { 
                _statusMessage = value; 
                StatusId = Guid.NewGuid();
                OnPropertyChanged(); 
                
                if (!string.IsNullOrEmpty(value))
                {
                    StartStatusTimer();
                }
            }
        }

        private void StartStatusTimer()
        {
            _statusCts?.Cancel();
            _statusCts = new CancellationTokenSource();
            var token = _statusCts.Token;

            Task.Delay(5000, token).ContinueWith(t =>
            {
                if (!t.IsCanceled)
                {
                    _statusMessage = string.Empty;
                    OnPropertyChanged(nameof(StatusMessage));
                }
            }, TaskScheduler.Default);
        }

        public string YoutubeUrl
        {
            get => _youtubeUrl;
            set 
            { 
                var finalValue = value;
                if (!string.IsNullOrWhiteSpace(value) && value.Contains("watch?v=") && value.Contains("&list="))
                {
                    finalValue = value.Split('&')[0];
                    if (finalValue != value)
                    {
                        YoutubeStatusMessage = "INFO: Cleaned YouTube URL (removed playlist parameters)";
                    }
                }

                _youtubeUrl = finalValue; 
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsYoutubeUrlNotEmpty));
                OnPropertyChanged(nameof(ShowDropbox));
                OnPropertyChanged(nameof(ShowPlaylistDropdown));
                OnPropertyChanged(nameof(IsYoutubePlaylistUrl));
                
                if (!string.IsNullOrWhiteSpace(finalValue) && (finalValue.Contains("list=") || finalValue.Contains("playlist?list=")))
                {
                    SelectedOrganizationGuid = null;
                }
            }
        }

        public async Task ClearYoutubeUrlAsync()
        {
            YoutubeUrl = string.Empty;
            await LoadOrganizationOptionsAsync();
        }

        public bool IsUploading
        {
            get => _isUploading;
            set { _isUploading = value; OnPropertyChanged(); }
        }

        public bool IsYoutubeDownloading
        {
            get => _isYoutubeDownloading;
            set { _isYoutubeDownloading = value; OnPropertyChanged(); }
        }

        public Guid StatusId
        {
            get => _statusId;
            private set { _statusId = value; OnPropertyChanged(); }
        }

        public string YoutubeStatusMessage
        {
            get => _youtubeStatusMessage;
            set 
            { 
                _youtubeStatusMessage = value; 
                OnPropertyChanged(); 
                
                if (!string.IsNullOrEmpty(value) && value.Contains("Cleaned YouTube URL"))
                {
                    StartYoutubeStatusTimer();
                }
            }
        }

        public ObservableCollection<YoutubeDownloadFailure> YoutubeDownloadFailures
        {
            get => _youtubeDownloadFailures;
            set { _youtubeDownloadFailures = value; OnPropertyChanged(); }
        }

        public bool IsYoutubeStatusExpanded
        {
            get => _isYoutubeStatusExpanded;
            set { _isYoutubeStatusExpanded = value; OnPropertyChanged(); }
        }

        public void ToggleYoutubeStatusExpansion()
        {
            if (IsYoutubeDownloading || !_youtubeDownloadFailures.Any()) return;
            IsYoutubeStatusExpanded = !IsYoutubeStatusExpanded;
        }

        private void StartYoutubeStatusTimer()
        {
            _youtubeStatusCts?.Cancel();
            _youtubeStatusCts = new CancellationTokenSource();
            var token = _youtubeStatusCts.Token;

            Task.Delay(5000, token).ContinueWith(t =>
            {
                if (!t.IsCanceled)
                {
                    _youtubeStatusMessage = string.Empty;
                    OnPropertyChanged(nameof(YoutubeStatusMessage));
                }
            }, TaskScheduler.Default);
        }

        public int UploadedCount
        {
            get => _uploadedCount;
            set { _uploadedCount = value; OnPropertyChanged(); }
        }

        public int TotalCount
        {
            get => _totalCount;
            set { _totalCount = value; OnPropertyChanged(); }
        }

        public bool ShowOrganization => SelectedMediaType != MediaType.None;
        public bool ShowPlaylistDropdown => ShowOrganization && !IsYoutubePlaylistUrl;
        public bool ShowDropbox => SelectedMediaType != MediaType.None && !IsYoutubeUrlNotEmpty;
        public bool ShowYoutubeSection => SelectedMediaType == MediaType.Audio || SelectedMediaType == MediaType.Video;
        public bool IsYoutubeUrlNotEmpty => !string.IsNullOrWhiteSpace(YoutubeUrl);
        public bool IsYoutubePlaylistUrl => !string.IsNullOrWhiteSpace(YoutubeUrl) && 
                                           (YoutubeUrl.Contains("list=") || YoutubeUrl.Contains("playlist?list="));

        public string YoutubeDownloadButtonLabel => SelectedMediaType switch
        {
            MediaType.Audio => "Download Audio",
            MediaType.Video => "Download Video",
            _ => "Download"
        };

        public string DropboxIcon => SelectedMediaType switch
        {
            MediaType.Audio => "♪",
            MediaType.Video => "🎞",
            _ => "📁"
        };

        public string DropboxLabel => SelectedMediaType switch
        {
            MediaType.Audio => "Drop audio files here (MP3, AAC, WAV, FLAC)",
            MediaType.Video => "Drop video files here (MP4, MKV, AVI, MOV, WEBM)",
            _ => "Select a media type first"
        };

        public string OrganizationLabel => SelectedMediaType switch
        {
            MediaType.Audio => "Playlist",
            MediaType.Video => "Playlist",
            _ => ""
        };

        public string AcceptedExtensions => SelectedMediaType != MediaType.None && ExtensionMap.ContainsKey(SelectedMediaType)
            ? string.Join(",", ExtensionMap[SelectedMediaType])
            : "";

        public static string Version { get; } = LoadVersion();

        private static string LoadVersion()
        {
            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
                var json = File.ReadAllText(path);
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.GetProperty("Version").GetString() ?? "unknown";
            }
            catch
            {
                return "unknown";
            }
        }

        #endregion

        public DashboardViewModel(
            IAudioService audioService,
            IVideoService videoService,
            ISettingsService settingsService,
            IYoutubeService youtubeService,
            IExternalToolService toolService)
        {
            _audioService = audioService;
            _videoService = videoService;
            _settingsService = settingsService;
            _youtubeService = youtubeService;
            _toolService = toolService;
        }

        public async Task DownloadYoutubeAudioAsync()
        {
            await DownloadYoutubeAsync(true);
        }

        public async Task DownloadYoutubeVideoAsync()
        {
            await DownloadYoutubeAsync(false);
        }

        private async Task DownloadYoutubeAsync(bool isAudio)
        {
            if (IsYoutubeDownloading) return;
            IsYoutubeDownloading = true;

            try
            {
                if (string.IsNullOrWhiteSpace(YoutubeUrl))
                {
                    YoutubeStatusMessage = "WARN: Please paste a YouTube URL first.";
                    return;
                }

                if (!await _toolService.IsToolInstalledAsync("yt-dlp"))
                {
                    YoutubeStatusMessage = "WARN: yt-dlp is not installed. Please check Development Tools.";
                    return;
                }

                YoutubeStatusMessage = "INFO: Starting YouTube download...";

                YoutubeDownloadFailures.Clear();
                IsYoutubeStatusExpanded = false;

                YoutubeDownloadResult result;
                if (isAudio)
                    result = await _youtubeService.DownloadAudioAsync(YoutubeUrl, msg => YoutubeStatusMessage = $"PROGRESS: {msg}", LoadOrganizationOptionsAsync, SelectedOrganizationGuid);
                else
                    result = await _youtubeService.DownloadVideoAsync(YoutubeUrl, msg => YoutubeStatusMessage = $"PROGRESS: {msg}", LoadOrganizationOptionsAsync, SelectedOrganizationGuid);
                
                if (result.Failures != null && result.Failures.Any())
                {
                    foreach (var fail in result.Failures)
                    {
                        YoutubeDownloadFailures.Add(fail);
                    }
                }

                var parts = new List<string>();
                if (result.Success > 0) parts.Add($"{result.Success} file(s) downloaded");
                if (result.Error > 0) parts.Add($"{result.Error} failed");
                
                if (result.Mapped > 0)
                {
                    parts.Add($"{result.Mapped} duplicate(s) added to playlist");
                }
                
                var simpleDuplicates = result.Duplicate - result.Mapped;
                if (simpleDuplicates > 0)
                {
                    parts.Add($"{simpleDuplicates} duplicate(s) skipped");
                }
                
                var summary = string.Join("  ·  ", parts);
                if (result.Error > 0) YoutubeStatusMessage = $"ERROR: {summary}";
                else if (result.Duplicate > 0) YoutubeStatusMessage = $"WARN: {summary}";
                else YoutubeStatusMessage = $"SUCCESS: {summary}";
            }
            catch (Exception ex)
            {
                YoutubeStatusMessage = $"ERROR: Download failed: {ex.Message}";
            }
            finally
            {
                IsYoutubeDownloading = false;
                YoutubeUrl = string.Empty;
            }
        }

        public void DismissYoutubeStatus()
        {
            YoutubeStatusMessage = string.Empty;
            YoutubeDownloadFailures.Clear();
            IsYoutubeStatusExpanded = false;
        }


        public async Task SelectMediaTypeAsync(MediaType type)
        {
            SelectedMediaType = type;
            SelectedOrganizationGuid = null;
            NewOrganizationName = string.Empty;
            StatusMessage = string.Empty;
            await LoadOrganizationOptionsAsync();
        }

        public async Task LoadOrganizationOptionsAsync()
        {
            var options = new List<OrganizationOption>();

            switch (SelectedMediaType)
            {
                case MediaType.Audio:
                    var playlists = await _audioService.GetPlaylistsAsync();
                    options = playlists.Select(p => new OrganizationOption { GUID = p.GUID, Name = p.Name }).ToList();
                    break;
                case MediaType.Video:
                    var vPlaylists = await _videoService.GetPlaylistsAsync();
                    options = vPlaylists.Select(p => new OrganizationOption { GUID = p.GUID, Name = p.Name }).ToList();
                    break;
            }

            OrganizationOptions = new ObservableCollection<OrganizationOption>(options);
        }

        public async Task CreateNewOrganizationAsync()
        {
            if (string.IsNullOrWhiteSpace(NewOrganizationName)) return;

            switch (SelectedMediaType)
            {
                case MediaType.Audio:
                    var existingPlaylists = await _audioService.GetPlaylistsAsync();
                    var existingAudioPlaylist = existingPlaylists.FirstOrDefault(p => string.Equals(p.Name, NewOrganizationName, StringComparison.OrdinalIgnoreCase));
                    if (existingAudioPlaylist != null)
                    {
                        SelectedOrganizationGuid = existingAudioPlaylist.GUID;
                        NewOrganizationName = string.Empty;
                        await LoadOrganizationOptionsAsync();
                        return;
                    }
                    var ap = await _audioService.CreatePlaylistAsync(NewOrganizationName);
                    SelectedOrganizationGuid = ap.GUID;
                    break;
                case MediaType.Video:
                    var existingVPlaylists = await _videoService.GetPlaylistsAsync();
                    var existingVideoPlaylist = existingVPlaylists.FirstOrDefault(p => string.Equals(p.Name, NewOrganizationName, StringComparison.OrdinalIgnoreCase));
                    if (existingVideoPlaylist != null)
                    {
                        SelectedOrganizationGuid = existingVideoPlaylist.GUID;
                        NewOrganizationName = string.Empty;
                        await LoadOrganizationOptionsAsync();
                        return;
                    }
                    var vp = await _videoService.CreatePlaylistAsync(NewOrganizationName);
                    SelectedOrganizationGuid = vp.GUID;
                    break;
            }

            NewOrganizationName = string.Empty;
            await LoadOrganizationOptionsAsync();
        }

        public async Task HandleFileSelectedAsync(IReadOnlyList<IBrowserFile> files)
        {
            if (SelectedMediaType == MediaType.None)
            {
                StatusMessage = "WARN: Please select a media type first.";
                return;
            }

            var libraryPath = await _settingsService.GetSettingAsync("LibraryPath");
            if (string.IsNullOrEmpty(libraryPath))
            {
                StatusMessage = "WARN: Library path not set. Please configure it in Settings first.";
                return;
            }

            var supportedExts = ExtensionMap[SelectedMediaType];
            var validFiles = files.Where(f => supportedExts.Contains(Path.GetExtension(f.Name))).ToList();
            var rejectedCount = files.Count - validFiles.Count;

            if (validFiles.Count == 0)
            {
                StatusMessage = $"WARN: No supported files found for {SelectedMediaType}.";
                return;
            }

            IsUploading = true;
            UploadedCount = 0;
            TotalCount = validFiles.Count;
            StatusMessage = $"INFO: Uploading {TotalCount} file(s)...";

            var successCount = 0;
            var errorCount = 0;
            var duplicateCount = 0;
            var mappedCount = 0;
            var tempDir = Path.Combine(Path.GetTempPath(), "Autonomuse_Upload");
            if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);

            foreach (var file in validFiles)
            {
                try
                {
                    var tempPath = Path.Combine(tempDir, file.Name);
                    await using (var stream = file.OpenReadStream(maxAllowedSize: 524_288_000))
                    await using (var fs = new FileStream(tempPath, FileMode.Create))
                    {
                        await stream.CopyToAsync(fs);
                    }

                    var result = await ProcessLocalFileAsync(file.Name, tempPath, true);
                    if (result.Success) successCount++;
                    if (result.Duplicate) duplicateCount++;
                    if (result.Mapped) mappedCount++;
                }
                catch (Exception ex)
                {
                    errorCount++;
                    System.Diagnostics.Debug.WriteLine($"Upload error for {file.Name}: {ex.Message}");
                }
                finally
                {
                    UploadedCount++;
                }
            }

            await FinalizeUploadBatchAsync(successCount, errorCount, duplicateCount, rejectedCount, mappedCount);

            // Reset InputFile component key to clear its state and fix drag-and-drop bug
            InputFileKey = Guid.NewGuid();
        }

        public async Task PickFilesNativeAsync()
        {
            if (SelectedMediaType == MediaType.None)
            {
                StatusMessage = "WARN: Please select a media type first.";
                return;
            }

            var libraryPath = await _settingsService.GetSettingAsync("LibraryPath");
            if (string.IsNullOrEmpty(libraryPath))
            {
                StatusMessage = "WARN: Library path not set. Please configure it in Settings first.";
                return;
            }

            var pickOptions = new PickOptions
            {
                PickerTitle = "Please select files"
            };

            var validExtensions = ExtensionMap[SelectedMediaType].Select(e => e.ToLowerInvariant()).ToList();
            if (DeviceInfo.Platform == DevicePlatform.WinUI)
            {
                var customFileType = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.WinUI, validExtensions }
                });
                pickOptions.FileTypes = customFileType;
            }

            try
            {
                var results = await FilePicker.Default.PickMultipleAsync(pickOptions);
                if (results != null && results.Any())
                {
                    var validFiles = results.Where(f => f != null).Where(f => validExtensions.Contains(Path.GetExtension(f!.FileName)?.ToLowerInvariant() ?? "")).ToList();
                    var rejectedCount = results.Count() - validFiles.Count;

                    if (validFiles.Count == 0)
                    {
                        StatusMessage = $"WARN: No supported files found for {SelectedMediaType}.";
                        return;
                    }

                    IsUploading = true;
                    UploadedCount = 0;
                    TotalCount = validFiles.Count;
                    StatusMessage = $"INFO: Uploading {TotalCount} file(s)...";

                    var successCount = 0;
                    var errorCount = 0;
                    var duplicateCount = 0;
                    var mappedCount = 0;
                    foreach (var file in validFiles)
                    {
                        try
                        {
                            var result = await ProcessLocalFileAsync(file!.FileName ?? "", file!.FullPath ?? "", false);
                            if (result.Success) successCount++;
                            if (result.Duplicate) duplicateCount++;
                            if (result.Mapped) mappedCount++;
                        }
                        catch (Exception ex)
                        {
                            errorCount++;
                            System.Diagnostics.Debug.WriteLine($"Upload error for {file!.FileName ?? ""}: {ex.Message}");
                        }
                        finally
                        {
                            UploadedCount++;
                        }
                    }

            await FinalizeUploadBatchAsync(successCount, errorCount, duplicateCount, rejectedCount, mappedCount);
                }
            }
            catch (Exception)
            {
                // User canceled the picker
            }
        }

        private string CleanYoutubeFileName(string fileName)
        {
            var ext = Path.GetExtension(fileName);
            var title = Path.GetFileNameWithoutExtension(fileName);
            int index = title.IndexOf("_youtube", StringComparison.OrdinalIgnoreCase);
            if (index > 0)
            {
                return title.Substring(0, index).Trim() + ext;
            }
            return fileName;
        }

        private async Task<(bool Success, bool Duplicate, bool Mapped)> ProcessLocalFileAsync(string fileName, string localPath, bool isTempFile)
        {
            bool skipUpload = false;
            
            // DEDUPLICATION LOGIC
            if (SelectedMediaType == MediaType.Audio)
            {
                fileName = CleanYoutubeFileName(fileName);
                var titleToCheck = Path.GetFileNameWithoutExtension(fileName);
                var existingRecord = await _audioService.GetAudioByTitleAndSourceAsync(titleToCheck, "manual upload");

                if (existingRecord != null)
                {
                    var existingPlaylists = await _audioService.GetPlaylistsForAudioAsync(existingRecord.GUID);

                    if (!string.IsNullOrEmpty(SelectedOrganizationGuid))
                    {
                        if (existingPlaylists.Contains(SelectedOrganizationGuid))
                        {
                            skipUpload = true;
                            return (Success: false, Duplicate: true, Mapped: false);
                        }
                        else
                        {
                            await _audioService.AddToPlaylistAsync(SelectedOrganizationGuid, existingRecord.GUID);
                            skipUpload = true;
                            return (Success: false, Duplicate: true, Mapped: true);
                        }
                    }
                    else
                    {
                        skipUpload = true;
                        return (Success: false, Duplicate: true, Mapped: false);
                    }
                }
            }

            if (SelectedMediaType == MediaType.Video)
            {
                fileName = CleanYoutubeFileName(fileName);
                var titleToCheck = Path.GetFileNameWithoutExtension(fileName);
                var existingRecord = await _videoService.GetVideoByTitleAndSourceAsync(titleToCheck, "manual upload");

                if (existingRecord != null)
                {
                    var existingPlaylists = await _videoService.GetPlaylistsForVideoAsync(existingRecord.GUID);

                    if (!string.IsNullOrEmpty(SelectedOrganizationGuid))
                    {
                        if (existingPlaylists.Contains(SelectedOrganizationGuid))
                        {
                            skipUpload = true;
                            return (Success: false, Duplicate: true, Mapped: false);
                        }
                        else
                        {
                            await _videoService.AddToPlaylistAsync(SelectedOrganizationGuid, existingRecord.GUID);
                            skipUpload = true;
                            return (Success: false, Duplicate: true, Mapped: true);
                        }
                    }
                    else
                    {
                        skipUpload = true;
                        return (Success: false, Duplicate: true, Mapped: false);
                    }
                }
            }

            if (skipUpload)
            {
                if (isTempFile && File.Exists(localPath)) File.Delete(localPath);
                return (Success: false, Duplicate: false, Mapped: false);
            }

            string recordGuid;

            switch (SelectedMediaType)
            {
                case MediaType.Audio:
                    {
                        var cleanPath = localPath;
                        if (isTempFile)
                        {
                            var dir = Path.GetDirectoryName(localPath);
                            var newPath = Path.Combine(dir ?? string.Empty, fileName);
                            if (localPath != newPath)
                            {
                                if (File.Exists(newPath)) File.Delete(newPath);
                                File.Move(localPath, newPath);
                                cleanPath = newPath;
                                localPath = newPath;
                            }
                        }
                        var audio = await _audioService.AddAudioAsync(cleanPath, "manual upload");
                        recordGuid = audio.GUID;
                        if (!string.IsNullOrEmpty(SelectedOrganizationGuid))
                            await _audioService.AddToPlaylistAsync(SelectedOrganizationGuid, recordGuid);
                        break;
                    }
                case MediaType.Video:
                    {
                        var cleanPath = localPath;
                        if (isTempFile)
                        {
                            var dir = Path.GetDirectoryName(localPath);
                            var newPath = Path.Combine(dir ?? string.Empty, fileName);
                            if (localPath != newPath)
                            {
                                if (File.Exists(newPath)) File.Delete(newPath);
                                File.Move(localPath, newPath);
                                cleanPath = newPath;
                                localPath = newPath;
                            }
                        }
                        var video = await _videoService.AddVideoAsync(cleanPath, "manual upload");
                        recordGuid = video.GUID;
                        if (!string.IsNullOrEmpty(SelectedOrganizationGuid))
                            await _videoService.AddToPlaylistAsync(SelectedOrganizationGuid, recordGuid);
                        break;
                    }
            }

            if (isTempFile && File.Exists(localPath)) File.Delete(localPath);
            return (Success: true, Duplicate: false, Mapped: false);
        }

        private async Task FinalizeUploadBatchAsync(int successCount, int errorCount, int duplicateCount, int rejectedCount, int mappedCount)
        {
            IsUploading = false;

            var parts = new List<string>();
            if (successCount > 0) parts.Add($"{successCount} file(s) uploaded");
            if (errorCount > 0) parts.Add($"{errorCount} failed");
            if (rejectedCount > 0) parts.Add($"{rejectedCount} unsupported format(s) skipped");
            
            if (mappedCount > 0)
            {
                parts.Add($"{mappedCount} duplicate(s) added to playlist");
            }
            
            var simpleDuplicates = duplicateCount - mappedCount;
            if (simpleDuplicates > 0)
            {
                parts.Add($"{simpleDuplicates} duplicate(s) skipped");
            }
            
            var summary = string.Join("  ·  ", parts);
            if (errorCount > 0) StatusMessage = $"ERROR: {summary}";
            else if (duplicateCount > 0) StatusMessage = $"WARN: {summary}";
            else StatusMessage = $"SUCCESS: {summary}";
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
