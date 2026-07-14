using System.Text.Json;
using Autonomuse.Shared.Contracts;
using Autonomuse.Shared.DTOs;
using SkiaSharp;

namespace Autonomuse.Services.Orchestration
{
    public class HomeUIService : IHomeUIService
    {
        private readonly ISettingsService _settingsService;
        private readonly string _backgroundFolder;
        private readonly string _thumbnailFolder;
        private readonly Dictionary<string, string> _base64Cache = new();
        private readonly Dictionary<string, string> _thumbCache = new();
        private HomeUISettings? _cachedSettings;

        public HomeUIService(ISettingsService settingsService)
        {
            _settingsService = settingsService;
            
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _backgroundFolder = Path.Combine(localAppData, "Autonomuse", "com.vantaseed.autonomuse", "Data", "UI Background");
            _thumbnailFolder = Path.Combine(_backgroundFolder, "thumbnail");
            
            Initialize();
        }

        public void Initialize()
        {
            if (!Directory.Exists(_backgroundFolder))
            {
                Directory.CreateDirectory(_backgroundFolder);
            }
            if (!Directory.Exists(_thumbnailFolder))
            {
                Directory.CreateDirectory(_thumbnailFolder);
            }
        }

        public async Task<HomeUISettings> GetSettingsAsync()
        {
            if (_cachedSettings != null) return _cachedSettings;

            var json = await _settingsService.GetSettingAsync("HomeUISettings");
            if (string.IsNullOrEmpty(json)) 
            {
                _cachedSettings = new HomeUISettings();
            }
            else
            {
                try
                {
                    _cachedSettings = JsonSerializer.Deserialize<HomeUISettings>(json) ?? new HomeUISettings();
                }
                catch
                {
                    _cachedSettings = new HomeUISettings();
                }
            }
            return _cachedSettings;
        }

        public async Task SaveSettingsAsync(HomeUISettings settings)
        {
            _cachedSettings = settings;
            var json = JsonSerializer.Serialize(settings);
            await _settingsService.SaveSettingAsync("HomeUISettings", json);
        }

        public async Task<string> SaveBackgroundAsync(string tabName, string sourcePath)
        {
            if (!File.Exists(sourcePath)) return string.Empty;

            try
            {
                var fileName = $"{tabName.ToLower()}.jpg";
                var destination = Path.Combine(_backgroundFolder, fileName);
                var thumbDest = Path.Combine(_thumbnailFolder, fileName);

                await Task.Run(() =>
                {
                    using var input = File.OpenRead(sourcePath);
                    using var inputStream = new SKManagedStream(input);
                    using var original = SKBitmap.Decode(inputStream);

                    if (original != null)
                    {
                        // Save Original (Compressed)
                        using (var image = SKImage.FromBitmap(original))
                        using (var data = image.Encode(SKEncodedImageFormat.Jpeg, 75))
                        using (var output = File.OpenWrite(destination))
                        {
                            data.SaveTo(output);
                        }

                        // Save Thumbnail (Lighter)
                        int thumbWidth = 150;
                        int thumbHeight = (int)(original.Height * (thumbWidth / (float)original.Width));
                        using (var resized = original.Resize(new SKImageInfo(thumbWidth, thumbHeight), SKSamplingOptions.Default))
                        using (var thumbImage = SKImage.FromBitmap(resized))
                        using (var thumbData = thumbImage.Encode(SKEncodedImageFormat.Jpeg, 60))
                        using (var thumbOutput = File.OpenWrite(thumbDest))
                        {
                            thumbData.SaveTo(thumbOutput);
                        }
                    }
                });

                lock (_base64Cache) { _base64Cache.Remove(tabName); }
                lock (_thumbCache) { _thumbCache.Remove(tabName); }
                await _settingsService.SaveSettingAsync($"Background_{tabName}", destination);

                return destination;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Compression failed: {ex.Message}");
                return string.Empty;
            }
        }

        public async Task RemoveBackgroundAsync(string tabName)
        {
            // Clear from DB
            await _settingsService.SaveSettingAsync($"Background_{tabName}", string.Empty);
            
            // Clear from Cache
            lock (_base64Cache) { _base64Cache.Remove(tabName); }
            lock (_thumbCache) { _thumbCache.Remove(tabName); }
            
            // Delete background file
            if (Directory.Exists(_backgroundFolder))
            {
                var existingFiles = Directory.GetFiles(_backgroundFolder, $"{tabName.ToLower()}.*");
                foreach (var file in existingFiles)
                {
                    try { File.Delete(file); } catch { }
                }
            }

            // Delete thumbnail file
            if (Directory.Exists(_thumbnailFolder))
            {
                var existingFiles = Directory.GetFiles(_thumbnailFolder, $"{tabName.ToLower()}.*");
                foreach (var file in existingFiles)
                {
                    try { File.Delete(file); } catch { }
                }
            }
        }

        public async Task<string> GetBackgroundAsBase64Async(string tabName)
        {
            lock (_base64Cache)
            {
                if (_base64Cache.TryGetValue(tabName, out var cached)) return cached;
            }

            var path = await _settingsService.GetSettingAsync($"Background_{tabName}");
            
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return string.Empty;

            try
            {
                var bytes = await File.ReadAllBytesAsync(path);
                var base64 = $"data:image/jpeg;base64,{Convert.ToBase64String(bytes)}";
                
                lock (_base64Cache) { _base64Cache[tabName] = base64; }
                
                return base64;
            }
            catch
            {
                return string.Empty;
            }
        }

        public async Task<string> GetThumbnailAsBase64Async(string tabName)
        {
            lock (_thumbCache)
            {
                if (_thumbCache.TryGetValue(tabName, out var cached)) return cached;
            }

            var path = Path.Combine(_thumbnailFolder, $"{tabName.ToLower()}.jpg");
            
            if (!File.Exists(path)) return string.Empty;

            try
            {
                var bytes = await File.ReadAllBytesAsync(path);
                var base64 = $"data:image/jpeg;base64,{Convert.ToBase64String(bytes)}";
                
                lock (_thumbCache) { _thumbCache[tabName] = base64; }
                
                return base64;
            }
            catch
            {
                return string.Empty;
            }
        }
        public async Task ResetAllBackgroundsAsync()
        {
            // Clear from Cache
            lock (_base64Cache) { _base64Cache.Clear(); }
            lock (_thumbCache) { _thumbCache.Clear(); }
            _cachedSettings = null;
            
            // Delete background files
            if (Directory.Exists(_backgroundFolder))
            {
                var files = Directory.GetFiles(_backgroundFolder);
                foreach (var file in files)
                {
                    try { File.Delete(file); } catch { }
                }
            }

            // Delete thumbnail files
            if (Directory.Exists(_thumbnailFolder))
            {
                var files = Directory.GetFiles(_thumbnailFolder);
                foreach (var file in files)
                {
                    try { File.Delete(file); } catch { }
                }
            }
            
            await Task.CompletedTask;
        }
    }
}
