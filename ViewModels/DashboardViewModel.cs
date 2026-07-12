using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Autonomuse.Shared.Contracts;
using Autonomuse.Shared.DTOs;
using Autonomuse.Shared.Enums;
using Microsoft.AspNetCore.Components.Forms;
using CoenM.ImageHash;
using CoenM.ImageHash.HashAlgorithms;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SkiaSharp;
using System.Diagnostics;
using System.IO;
namespace Autonomuse.ViewModels
{


    public class DashboardViewModel : INotifyPropertyChanged
    {
        private readonly IAudioService _audioService;
        private readonly IVideoService _videoService;
        private readonly IImageService _imageService;
        private readonly IEBookService _ebookService;
        private readonly ISettingsService _settingsService;
        private readonly IYoutubeService _youtubeService;
        private readonly IExternalToolService _toolService;

        private MediaType _selectedMediaType = MediaType.Audio;
        private string? _selectedOrganizationGuid;
        private string? _selectedChapterGuid;
        private string _newOrganizationName = string.Empty;
        private int? _chapterNumber;
        private string _chapterTitle = string.Empty;
        private string _statusMessage = string.Empty;
        private string _youtubeUrl = string.Empty;
        private bool _isUploading;
        private bool _isYoutubeDownloading;
        private string _youtubeStatusMessage = string.Empty;
        private int _uploadedCount;
        private int _totalCount;
        private ObservableCollection<OrganizationOption> _organizationOptions = new();
        private int _imageCompressionLevel;
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
            [MediaType.Images] = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp" },
            [MediaType.EBooks] = new(StringComparer.OrdinalIgnoreCase) { ".pdf", ".epub", ".cbz", ".cbr", ".mobi", ".jpg", ".jpeg", ".png" }
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
                OnPropertyChanged(nameof(ShowChapterFields)); 
                OnPropertyChanged(nameof(ShowDropbox)); 
                OnPropertyChanged(nameof(DropboxIcon)); 
                OnPropertyChanged(nameof(DropboxLabel)); 
                OnPropertyChanged(nameof(OrganizationLabel)); 
                OnPropertyChanged(nameof(AcceptedExtensions));
                OnPropertyChanged(nameof(ShowYoutubeSection));
                OnPropertyChanged(nameof(YoutubeDownloadButtonLabel));

                if (value == MediaType.EBooks)
                {
                    _ = LoadChaptersAsync();
                }
            }
        }

        public string? SelectedOrganizationGuid
        {
            get => _selectedOrganizationGuid;
            set 
            { 
                _selectedOrganizationGuid = value; 
                OnPropertyChanged();
                if (SelectedMediaType == MediaType.EBooks)
                {
                    _ = LoadChaptersAsync();
                }
            }
        }

        public string? SelectedChapterGuid
        {
            get => _selectedChapterGuid;
            set { _selectedChapterGuid = value; OnPropertyChanged(); }
        }

        private ObservableCollection<OrganizationOption> _chapters = new();
        public ObservableCollection<OrganizationOption> Chapters
        {
            get => _chapters;
            set { _chapters = value; OnPropertyChanged(); }
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

        public int? ChapterNumber
        {
            get => _chapterNumber;
            set { _chapterNumber = value; OnPropertyChanged(); }
        }

        public string ChapterTitle
        {
            get => _chapterTitle;
            set { _chapterTitle = value; OnPropertyChanged(); }
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

        // Computed properties
        public int ImageCompressionLevel
        {
            get => _imageCompressionLevel;
            set { _imageCompressionLevel = value; OnPropertyChanged(); }
        }

        public bool ShowOrganization => SelectedMediaType != MediaType.None;
        public bool ShowPlaylistDropdown => ShowOrganization && !IsYoutubePlaylistUrl;
        public bool ShowChapterFields => SelectedMediaType == MediaType.EBooks;
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
            MediaType.Images => "🖼",
            MediaType.EBooks => "🕮",
            _ => "📁"
        };

        public string DropboxLabel => SelectedMediaType switch
        {
            MediaType.Audio => "Drop audio files here (MP3, AAC, WAV, FLAC)",
            MediaType.Video => "Drop video files here (MP4, MKV, AVI, MOV, WEBM)",
            MediaType.Images => "Drop image files here (JPG, PNG, GIF, WEBP, BMP)",
            MediaType.EBooks => "Drop book files or manga pages here (PDF, EPUB, CBZ, CBR, MOBI, JPG, PNG)",
            _ => "Select a media type first"
        };

        public string OrganizationLabel => SelectedMediaType switch
        {
            MediaType.Audio => "Playlist",
            MediaType.Video => "Playlist",
            MediaType.Images => "Album",
            MediaType.EBooks => "Series",
            _ => ""
        };

        public string AcceptedExtensions => SelectedMediaType != MediaType.None && ExtensionMap.ContainsKey(SelectedMediaType)
            ? string.Join(",", ExtensionMap[SelectedMediaType])
            : "";

        public bool IsImageFeatureEnabled => _settingsService.IsImageFeatureEnabled;
        public bool IsEbookFeatureEnabled => _settingsService.IsEbookFeatureEnabled;

        #endregion

        public DashboardViewModel(
            IAudioService audioService,
            IVideoService videoService,
            IImageService imageService,
            IEBookService ebookService,
            ISettingsService settingsService,
            IYoutubeService youtubeService,
            IExternalToolService toolService)
        {
            _audioService = audioService;
            _videoService = videoService;
            _imageService = imageService;
            _ebookService = ebookService;
            _settingsService = settingsService;
            _youtubeService = youtubeService;
            _toolService = toolService;

            _ = RefreshSettingsAsync();
        }

        public async Task RefreshSettingsAsync()
        {
            var compressionStr = await _settingsService.GetSettingAsync("ImageCompressionLevel");
            if (int.TryParse(compressionStr, out var level))
            {
                ImageCompressionLevel = level;
            }
            else
            {
                ImageCompressionLevel = 0;
            }
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
                case MediaType.Images:
                    var albums = await _imageService.GetAlbumsAsync();
                    options = albums.Select(a => new OrganizationOption { GUID = a.GUID, Name = a.Name }).ToList();
                    break;
                case MediaType.EBooks:
                    var series = await _ebookService.GetSeriesAsync();
                    options = series.Select(s => new OrganizationOption { GUID = s.GUID, Name = s.Name }).ToList();
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
                case MediaType.Images:
                    var existingAlbums = await _imageService.GetAlbumsAsync();
                    var existingAlbum = existingAlbums.FirstOrDefault(p => string.Equals(p.Name, NewOrganizationName, StringComparison.OrdinalIgnoreCase));
                    if (existingAlbum != null)
                    {
                        SelectedOrganizationGuid = existingAlbum.GUID;
                        NewOrganizationName = string.Empty;
                        await LoadOrganizationOptionsAsync();
                        return;
                    }
                    var album = await _imageService.CreateAlbumAsync(NewOrganizationName);
                    SelectedOrganizationGuid = album.GUID;
                    break;
                case MediaType.EBooks:
                    var ep = await _ebookService.CreateSeriesAsync(NewOrganizationName);
                    SelectedOrganizationGuid = ep.GUID;
                    break;
            }

            NewOrganizationName = string.Empty;
            await LoadOrganizationOptionsAsync();
        }

        public async Task LoadChaptersAsync()
        {
            if (string.IsNullOrEmpty(SelectedOrganizationGuid) || SelectedMediaType != MediaType.EBooks)
            {
                Chapters = new ObservableCollection<OrganizationOption>();
                SelectedChapterGuid = null;
                return;
            }

            try
            {
                var chapters = await _ebookService.GetChaptersBySeriesAsync(SelectedOrganizationGuid);
                
                // Sort descending by chapter number so latest is first
                var sortedChapters = chapters.OrderByDescending(c => c.ChapterNumber).ToList();

                Chapters = new ObservableCollection<OrganizationOption>(
                    sortedChapters.Select(c => new OrganizationOption { GUID = c.GUID, Name = $"Ch. {c.ChapterNumber} - {c.Title}" })
                );

                // Auto-select latest chapter
                if (Chapters.Any())
                {
                    SelectedChapterGuid = Chapters.First().GUID;
                }
                else
                {
                    SelectedChapterGuid = null;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading chapters: {ex.Message}");
            }
        }

        public async Task CreateNewChapterAsync()
        {
            if (string.IsNullOrEmpty(SelectedOrganizationGuid))
            {
                StatusMessage = "WARN: Please select a series first.";
                return;
            }
            if (string.IsNullOrWhiteSpace(ChapterTitle))
            {
                StatusMessage = "WARN: Chapter name is required.";
                return;
            }

            try
            {
                int num = ChapterNumber ?? 1;
                var ch = await _ebookService.CreateChapterAsync(SelectedOrganizationGuid, num, ChapterTitle);
                await LoadChaptersAsync();
                SelectedChapterGuid = ch.GUID;
                StatusMessage = $"SUCCESS: Chapter '{ChapterTitle}' created.";
                ChapterTitle = string.Empty;
                ChapterNumber = null; // Reset to show placeholder
            }
            catch (Exception ex)
            {
                StatusMessage = $"ERROR: {ex.Message}";
            }
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

            var mangaPagePaths = new List<string>();

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

                    var result = await ProcessLocalFileAsync(file.Name, tempPath, true, mangaPagePaths);
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

            await FinalizeUploadBatchAsync(mangaPagePaths, successCount, errorCount, duplicateCount, rejectedCount, mappedCount);

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
                    var mangaPagePaths = new List<string>();

                    foreach (var file in validFiles)
                    {
                        try
                        {
                            var result = await ProcessLocalFileAsync(file!.FileName ?? "", file!.FullPath ?? "", false, mangaPagePaths);
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

                    await FinalizeUploadBatchAsync(mangaPagePaths, successCount, errorCount, duplicateCount, rejectedCount, mappedCount);
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

        private async Task<(bool Success, bool Duplicate, bool Mapped)> ProcessLocalFileAsync(string fileName, string localPath, bool isTempFile, List<string> mangaPagePaths)
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

            ulong? generatedImageHash = null;

            if (SelectedMediaType == MediaType.Images)
            {
                var titleToCheck = Path.GetFileNameWithoutExtension(fileName);
                var extension = Path.GetExtension(fileName).ToLowerInvariant();
                var existingImages = await _imageService.GetImagesByTitleAndSourceAsync(titleToCheck, "manual upload");

                bool isDuplicate = false;
                string? duplicateGuid = null;

                if (existingImages.Any())
                {
                    var sameExtImages = existingImages.Where(i => i.Extension.Equals(extension, StringComparison.OrdinalIgnoreCase)).ToList();

                    if (sameExtImages.Any())
                    {
                        var hashAlgorithm = new AverageHash();
                        try
                        {
                            using var stream = File.OpenRead(localPath);
                            using var imageToHash = SixLabors.ImageSharp.Image.Load<Rgba32>(stream);
                            generatedImageHash = hashAlgorithm.Hash(imageToHash);
                            
                            foreach (var existImg in sameExtImages)
                            {
                                if (existImg.PerceptualHash.HasValue)
                                {
                                    var similarity = CompareHash.Similarity(generatedImageHash.Value, existImg.PerceptualHash.Value);
                                    if (similarity > 80)
                                    {
                                        isDuplicate = true;
                                        duplicateGuid = existImg.GUID;
                                        break;
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error hashing image: {ex.Message}");
                        }
                    }
                }

                if (!isDuplicate && !generatedImageHash.HasValue)
                {
                    try
                    {
                        var hashAlgorithm = new AverageHash();
                        using var stream = File.OpenRead(localPath);
                        using var imageToHash = SixLabors.ImageSharp.Image.Load<Rgba32>(stream);
                        generatedImageHash = hashAlgorithm.Hash(imageToHash);
                    }
                    catch { }
                }

                if (isDuplicate)
                {
                    var existingAlbums = await _imageService.GetAlbumsForImageAsync(duplicateGuid!);

                    if (!string.IsNullOrEmpty(SelectedOrganizationGuid))
                    {
                        if (existingAlbums.Contains(SelectedOrganizationGuid))
                        {
                            skipUpload = true;
                            return (Success: false, Duplicate: true, Mapped: false);
                        }
                        else
                        {
                            await _imageService.AddToAlbumAsync(SelectedOrganizationGuid!, duplicateGuid!);
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
                case MediaType.Images:
                    {
                        var uploadPath = localPath;
                        bool wasCompressed = false;
                        if (ImageCompressionLevel > 0)
                        {
                            var compressedPath = await CompressImageAsync(localPath, ImageCompressionLevel);
                            if (compressedPath != localPath)
                            {
                                uploadPath = compressedPath;
                                wasCompressed = true;
                            }
                        }

                        var image = await _imageService.AddImageAsync(uploadPath, "manual upload", generatedImageHash);
                        recordGuid = image.GUID;
                        
                        if (wasCompressed && File.Exists(uploadPath))
                        {
                            File.Delete(uploadPath);
                            var dir = Path.GetDirectoryName(uploadPath);
                            if (dir != null && Path.GetFileName(dir).StartsWith("Autonomuse_"))
                            {
                                try { Directory.Delete(dir, true); } catch { }
                            }
                        }

                        if (!string.IsNullOrEmpty(SelectedOrganizationGuid))
                            await _imageService.AddToAlbumAsync(SelectedOrganizationGuid!, recordGuid);
                        break;
                    }
                case MediaType.EBooks:
                    var ext = Path.GetExtension(fileName).ToLowerInvariant();
                    bool isMangaPage = (ext == ".jpg" || ext == ".jpeg" || ext == ".png")
                        && !string.IsNullOrEmpty(SelectedOrganizationGuid);

                    if (isMangaPage)
                    {
                        mangaPagePaths.Add(localPath);
                        // Do not delete temp manga pages here; handled in Finalize
                        isTempFile = false; 
                    }
                    else
                    {
                        await _ebookService.AddEBookAsync(localPath, "manual upload", SelectedOrganizationGuid, SelectedChapterGuid);
                    }
                    break;
            }

            if (isTempFile && File.Exists(localPath)) File.Delete(localPath);
            return (Success: true, Duplicate: false, Mapped: false);
        }

        private async Task FinalizeUploadBatchAsync(List<string> mangaPagePaths, int successCount, int errorCount, int duplicateCount, int rejectedCount, int mappedCount)
        {
            if (SelectedMediaType == MediaType.EBooks && mangaPagePaths.Count > 0 && !string.IsNullOrEmpty(SelectedOrganizationGuid))
            {
                try
                {
                    string? finalChapterGuid = SelectedChapterGuid;
                    if (string.IsNullOrEmpty(finalChapterGuid))
                    {
                        var chapter = await _ebookService.CreateChapterAsync(
                            SelectedOrganizationGuid, ChapterNumber ?? 1,
                            string.IsNullOrWhiteSpace(ChapterTitle) ? $"Chapter {ChapterNumber ?? 1}" : ChapterTitle);
                        finalChapterGuid = chapter.GUID;
                        await LoadChaptersAsync();
                        SelectedChapterGuid = finalChapterGuid;
                    }

                    await _ebookService.AddPagesToChapterAsync(finalChapterGuid, mangaPagePaths);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Chapter creation error: {ex.Message}");
                }

                // Cleanup manga temp files
                foreach (var tp in mangaPagePaths)
                {
                    if (File.Exists(tp)) File.Delete(tp);
                }
            }

            IsUploading = false;

            var parts = new List<string>();
            if (successCount > 0) parts.Add($"{successCount} file(s) uploaded");
            if (errorCount > 0) parts.Add($"{errorCount} failed");
            if (rejectedCount > 0) parts.Add($"{rejectedCount} unsupported format(s) skipped");
            
            if (mappedCount > 0)
            {
                var orgType = SelectedMediaType == MediaType.Images ? "album" : "playlist";
                parts.Add($"{mappedCount} duplicate(s) added to {orgType}");
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

        private async Task<string> CompressImageAsync(string sourcePath, int compressionLevel)
        {
            if (compressionLevel <= 0) return sourcePath;

            try
            {
                var originalFileName = Path.GetFileName(sourcePath);
                // Create a unique subfolder in temp to avoid filename collisions while preserving original filename
                var tempSubFolder = Path.Combine(Path.GetTempPath(), "Autonomuse_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempSubFolder);
                var tempPath = Path.Combine(tempSubFolder, originalFileName);

                await Task.Run(() =>
                {
                    using var stream = File.OpenRead(sourcePath);
                    using var managedStream = new SKManagedStream(stream);
                    using var bitmap = SKBitmap.Decode(managedStream);

                    if (bitmap == null) throw new Exception("Failed to decode image for compression.");

                    using var image = SKImage.FromBitmap(bitmap);
                    using var data = image.Encode(SKEncodedImageFormat.Jpeg, 100 - compressionLevel);

                    using var saveStream = File.OpenWrite(tempPath);
                    data.SaveTo(saveStream);
                });

                return tempPath;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Compression failed: {ex.Message}");
                return sourcePath;
            }
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
