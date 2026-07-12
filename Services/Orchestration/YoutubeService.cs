using System.Text.Json;
using Autonomuse.Domain.Entities;
using Autonomuse.Shared.Contracts;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using System.Net.Http;
using TagLib;

namespace Autonomuse.Services.Orchestration
{
    public class YoutubeService : IYoutubeService
    {
        private readonly IExternalToolService _toolService;
        private readonly IAudioService _audioService;
        private readonly IVideoService _videoService;
        private readonly ISettingsService _settingsService;
        private readonly ILogger<YoutubeService> _logger;
        private readonly HttpClient _httpClient = new();

        public YoutubeService(
            IExternalToolService toolService,
            IAudioService audioService,
            IVideoService videoService,
            ISettingsService settingsService,
            ILogger<YoutubeService> logger)
        {
            _toolService = toolService;
            _audioService = audioService;
            _videoService = videoService;
            _settingsService = settingsService;
            _logger = logger;
        }

        public async Task<YoutubeDownloadResult> DownloadAudioAsync(string url, Action<string>? onProgress = null, Func<Task>? onPlaylistCreated = null, string? manualPlaylistGuid = null)
        {
            return await DownloadMediaAsync(url, true, onProgress, onPlaylistCreated, manualPlaylistGuid);
        }

        public async Task<YoutubeDownloadResult> DownloadVideoAsync(string url, Action<string>? onProgress = null, Func<Task>? onPlaylistCreated = null, string? manualPlaylistGuid = null)
        {
            return await DownloadMediaAsync(url, false, onProgress, onPlaylistCreated, manualPlaylistGuid);
        }

        private async Task<YoutubeDownloadResult> DownloadMediaAsync(string url, bool isAudio, Action<string>? onProgress, Func<Task>? onPlaylistCreated, string? manualPlaylistGuid = null)
        {
            var libraryPath = await _settingsService.GetSettingAsync("LibraryPath");
            if (string.IsNullOrEmpty(libraryPath)) throw new InvalidOperationException("Library path not set.");

            var categoryFolder = isAudio ? "Audio" : "Video";
            var targetFolder = Path.Combine(libraryPath, categoryFolder);
            if (!Directory.Exists(targetFolder)) Directory.CreateDirectory(targetFolder);

            // Ensure Art directory exists
            var artBaseFolder = Path.Combine(libraryPath, "Art", categoryFolder);
            if (!Directory.Exists(artBaseFolder)) Directory.CreateDirectory(artBaseFolder);

            onProgress?.Invoke("Extracting metadata...");
            
            var metaArgs = $"--dump-json --flat-playlist --no-warnings \"{url}\"";
            var (code, stdout, stderr) = await _toolService.RunCommandAsync("yt-dlp", metaArgs);

            if (code != 0)
            {
                _logger.LogError("yt-dlp metadata error: {Error}", stderr);
                throw new Exception($"Failed to extract metadata: {stderr}");
            }

            var jsonLines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var items = jsonLines.Select(line => JsonSerializer.Deserialize<YtDlpInfo>(line)).Where(x => x != null).ToList();

            if (!items.Any()) throw new Exception("No media found at URL.");

            string? playlistGuid = null;
            if (items.Count > 1 || !string.IsNullOrEmpty(items[0]?.playlist_title))
            {
                var playlistName = items[0]?.playlist_title ?? "YouTube Download";
                onProgress?.Invoke($"Creating playlist: {playlistName}");
                
                if (isAudio)
                {
                    var pl = await _audioService.CreatePlaylistAsync(playlistName, url);
                    playlistGuid = pl.GUID;
                }
                else
                {
                    var pl = await _videoService.CreatePlaylistAsync(playlistName, url);
                    playlistGuid = pl.GUID;
                }

                if (onPlaylistCreated != null)
                {
                    await onPlaylistCreated.Invoke();
                }
            }

            int current = 0;
            int successCount = 0;
            int duplicateCount = 0;
            int mappedCount = 0;
            int errorCount = 0;
            var failures = new List<YoutubeDownloadFailure>();
            foreach (var item in items)
            {
                current++;
                onProgress?.Invoke($"[{current}/{items.Count}] Downloading: {item?.title}...");

                // Check if already exists in DB by ID
                if (isAudio)
                {
                    var existing = await _audioService.GetAudioByYoutubeIDAsync(item?.id ?? string.Empty);
                    if (existing != null)
                    {
                        bool wasMapped = false;
                        if (!string.IsNullOrEmpty(playlistGuid))
                        {
                            var plItems = await _audioService.GetPlaylistsForAudioAsync(existing.GUID);
                            if (!plItems.Contains(playlistGuid))
                            {
                                await _audioService.AddToPlaylistAsync(playlistGuid, existing.GUID);
                                wasMapped = true;
                            }
                        }
                        
                        // Also check manual playlist
                        if (!string.IsNullOrEmpty(manualPlaylistGuid))
                        {
                            var plItems = await _audioService.GetPlaylistsForAudioAsync(existing.GUID);
                            if (!plItems.Contains(manualPlaylistGuid))
                            {
                                await _audioService.AddToPlaylistAsync(manualPlaylistGuid, existing.GUID);
                                wasMapped = true;
                            }
                        }

                        if (wasMapped) mappedCount++;
                        duplicateCount++;
                        onProgress?.Invoke($"[{current}/{items.Count}] Already exists: {item?.title}");
                        continue;
                    }
                }
                else
                {
                    var existing = await _videoService.GetVideoByYoutubeIDAsync(item?.id ?? string.Empty);
                    if (existing != null)
                    {
                        bool wasMapped = false;
                        if (!string.IsNullOrEmpty(playlistGuid))
                        {
                            var plItems = await _videoService.GetPlaylistsForVideoAsync(existing.GUID);
                            if (!plItems.Contains(playlistGuid))
                            {
                                await _videoService.AddToPlaylistAsync(playlistGuid, existing.GUID);
                                wasMapped = true;
                            }
                        }
                        
                        // Also check manual playlist
                        if (!string.IsNullOrEmpty(manualPlaylistGuid))
                        {
                            var plItems = await _videoService.GetPlaylistsForVideoAsync(existing.GUID);
                            if (!plItems.Contains(manualPlaylistGuid))
                            {
                                await _videoService.AddToPlaylistAsync(manualPlaylistGuid, existing.GUID);
                                wasMapped = true;
                            }
                        }

                        if (wasMapped) mappedCount++;
                        duplicateCount++;
                        onProgress?.Invoke($"[{current}/{items.Count}] Already exists: {item?.title}");
                        continue;
                    }
                }

                var itemUrl = $"https://www.youtube.com/watch?v={item?.id}";
                
                // Get Original metadata
                var itemMetaArgs = $"--dump-json --no-warnings \"{itemUrl}\"";
                var (mCode, mStdout, mStderr) = await _toolService.RunCommandAsync("yt-dlp", itemMetaArgs);
                var fullMeta = mCode == 0 ? JsonSerializer.Deserialize<YtDlpInfo>(mStdout) : item;

                // Get English metadata for AlternativeTitle
                var enMetaArgs = $"--dump-json --no-warnings --extractor-args \"youtube:lang=en\" \"{itemUrl}\"";
                var (enCode, enStdout, enStderr) = await _toolService.RunCommandAsync("yt-dlp", enMetaArgs);
                var enMeta = enCode == 0 ? JsonSerializer.Deserialize<YtDlpInfo>(enStdout) : null;

                var rawTitle = fullMeta?.title ?? item?.title ?? "Unknown";
                var englishTitle = enMeta?.title ?? rawTitle;
                var altTitle = (englishTitle == rawTitle) ? rawTitle : englishTitle;

                // Extract Genre and Year
                string? genre = fullMeta?.categories?.FirstOrDefault();
                int? year = null;
                if (!string.IsNullOrEmpty(fullMeta?.upload_date) && fullMeta.upload_date.Length >= 4)
                {
                    if (int.TryParse(fullMeta.upload_date[..4], out int y)) year = y;
                }

                string downloadArgs;
                if (isAudio)
                {
                    // Download as ID first to be safe
                    downloadArgs = $"-x --audio-format mp3 --audio-quality 0 --add-metadata --embed-thumbnail --print after_move:filepath -o \"{targetFolder}/%(id)s.%(ext)s\" \"{itemUrl}\"";
                }
                else
                {
                    downloadArgs = $"-f \"bestvideo[height<=1080]+bestaudio/best[height<=1080]/best\" --merge-output-format mp4 --print after_move:filepath -o \"{targetFolder}/%(id)s.%(ext)s\" \"{itemUrl}\"";
                }

                int maxRetries = 3;
                int currentTry = 0;
                int dCode = -1;
                string dStdout = string.Empty;
                string dStderr = string.Empty;

                while (currentTry < maxRetries)
                {
                    currentTry++;
                    var result = await _toolService.RunCommandAsync("yt-dlp", downloadArgs);
                    dCode = result.ExitCode;
                    dStdout = result.StandardOutput;
                    dStderr = result.StandardError;

                    if (dCode == 0)
                    {
                        break;
                    }

                    if (currentTry < maxRetries)
                    {
                        _logger.LogWarning("Download attempt {Attempt} failed for {Id}. Retrying in 2 seconds... Error: {Error}", currentTry, item?.id, dStderr);
                        await Task.Delay(2000);
                    }
                }

                if (dCode != 0)
                {
                    _logger.LogWarning("Failed to download {Id} after {Max} attempts: {Error}", item?.id, maxRetries, dStderr);
                    errorCount++;
                    failures.Add(new YoutubeDownloadFailure
                    {
                        Title = rawTitle,
                        Error = string.IsNullOrWhiteSpace(dStderr) ? "Unknown yt-dlp error" : dStderr.Trim()
                    });
                    continue;
                }

                // 3. Get the downloaded ID-based path
                var tempFile = dStdout.Trim().Split('\n').LastOrDefault()?.Trim();
                if (string.IsNullOrEmpty(tempFile) || !System.IO.File.Exists(tempFile)) continue;

                // 4. Rename to Beautiful Title (Sanitized) with YouTube suffix
                var safeTitle = PrettifyFilename(rawTitle);
                var ext = Path.GetExtension(tempFile);
                var finalPath = Path.Combine(targetFolder, $"{safeTitle}_youtube_{item?.id}{ext}");

                // Handle collision if file already exists (should be rare with ID suffix)
                if (System.IO.File.Exists(finalPath))
                {
                    finalPath = Path.Combine(targetFolder, $"{safeTitle}_youtube_{item?.id}_{Guid.NewGuid().ToString()[..4]}{ext}");
                }

                try
                {
                    System.IO.File.Move(tempFile, finalPath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Failed to rename {Temp} to {Final}: {Ex}", tempFile, finalPath, ex.Message);
                    finalPath = tempFile; // Fallback to ID-based name
                }

                var guid = Guid.NewGuid().ToString();
                
                // 6. Process Art
                string? artPath = null;
                if (!string.IsNullOrEmpty(fullMeta?.thumbnail))
                {
                    try
                    {
                        var artFileName = $"{guid}.jpg";
                        var artDestination = Path.Combine(artBaseFolder, artFileName);
                        await ProcessThumbnailAsync(fullMeta.thumbnail, artDestination, isAudio);
                        artPath = artDestination;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("Failed to process thumbnail for {Id}: {Ex}", item?.id, ex.Message);
                    }
                }

                // Make path absolute (as requested)
                var finalFilePath = finalPath;

                // 5. Record to Database
                if (isAudio)
                {
                    var record = new AudioRecord
                    {
                        GUID = guid,
                        FileName = Path.GetFileName(finalPath),
                        Title = rawTitle,
                        AlternativeTitle = altTitle,
                        Extension = Path.GetExtension(finalPath),
                        Source = "youtube",
                        YoutubeID = fullMeta?.id ?? item?.id,
                        FilePath = finalFilePath,
                        Artist = fullMeta?.uploader,
                        Genre = genre,
                        Year = year,
                        FileSize = new FileInfo(finalPath).Length,
                        CoverArtPath = artPath,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };

                    // Metadata extraction from file properties (Mandatory for Resolution, Bitrate, Channels)
                    try
                    {
                        using var tfile = TagLib.File.Create(finalPath);
                        record.Duration = tfile.Properties?.Duration.TotalSeconds ?? fullMeta?.duration;
                        
                        if (tfile.Properties != null)
                        {
                            record.Channels = tfile.Properties.AudioChannels;
                            record.SampleRate = tfile.Properties.AudioSampleRate;
                            record.Bitrate = tfile.Properties.AudioBitrate;
                        }

                        if ((record.Bitrate ?? 0) == 0 && fullMeta?.abr > 0)
                            record.Bitrate = (int)Math.Round(fullMeta.abr.Value);
                    }
                    catch (Exception ex) { _logger.LogWarning("Audio meta extraction failed: {Ex}", ex.Message); }

                    // Generate Fingerprint for YouTube Audio
                    record.Fingerprint = await _audioService.GenerateFingerprintAsync(record.FilePath);

                    await _audioService.AddAudioRecordAsync(record);
                    successCount++;
                    if (!string.IsNullOrEmpty(playlistGuid))
                        await _audioService.AddToPlaylistAsync(playlistGuid, record.GUID);
                    if (!string.IsNullOrEmpty(manualPlaylistGuid))
                        await _audioService.AddToPlaylistAsync(manualPlaylistGuid, record.GUID);
                }
                else
                {
                    var record = new VideoRecord
                    {
                        GUID = guid,
                        FileName = Path.GetFileName(finalPath),
                        Title = rawTitle,
                        AlternativeTitle = altTitle,
                        Extension = Path.GetExtension(finalPath),
                        Source = "youtube",
                        YoutubeID = fullMeta?.id ?? item?.id,
                        FilePath = finalFilePath,
                        Genre = genre,
                        Artist = fullMeta?.uploader,
                        Year = year,
                        FileSize = new FileInfo(finalPath).Length,
                        ThumbnailPath = artPath,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };

                    // Metadata extraction from file properties (Mandatory for Resolution, Bitrate, Channels)
                    try
                    {
                        using var tfile = TagLib.File.Create(finalPath);
                        record.Duration = tfile.Properties?.Duration.TotalSeconds ?? fullMeta?.duration;

                        if (tfile.Properties != null)
                        {
                            record.Resolution = tfile.Properties.VideoWidth > 0 ? $"{tfile.Properties.VideoWidth}x{tfile.Properties.VideoHeight}" : null;
                            record.Bitrate = tfile.Properties.AudioBitrate;
                            record.SampleRate = tfile.Properties.AudioSampleRate;
                            record.Channels = tfile.Properties.AudioChannels;
                        }

                        if ((record.Bitrate ?? 0) == 0 && fullMeta?.abr > 0)
                            record.Bitrate = (int)Math.Round(fullMeta.abr.Value);
                    }
                    catch (Exception ex) { _logger.LogWarning("Video meta extraction failed: {Ex}", ex.Message); }

                    await _videoService.AddVideoRecordAsync(record);
                    successCount++;
                    if (!string.IsNullOrEmpty(playlistGuid))
                        await _videoService.AddToPlaylistAsync(playlistGuid, record.GUID);
                    if (!string.IsNullOrEmpty(manualPlaylistGuid))
                        await _videoService.AddToPlaylistAsync(manualPlaylistGuid, record.GUID);
                }
            }

            onProgress?.Invoke($"Successfully downloaded {current} item(s) from YouTube.");
            return new YoutubeDownloadResult
            {
                Success = successCount,
                Duplicate = duplicateCount,
                Mapped = mappedCount,
                Error = errorCount,
                Failures = failures
            };
        }

        private async Task ProcessThumbnailAsync(string thumbnailUrl, string destinationPath, bool cropSquare)
        {
            var bytes = await _httpClient.GetByteArrayAsync(thumbnailUrl);
            using var inputStream = new MemoryStream(bytes);
            using var original = SKBitmap.Decode(inputStream);
            
            if (original == null) return;

            if (cropSquare)
            {
                int size = Math.Min(original.Width, original.Height);
                int x = (original.Width - size) / 2;
                int y = (original.Height - size) / 2;

                using var subset = new SKBitmap(size, size);
                original.ExtractSubset(subset, new SKRectI(x, y, x + size, y + size));
                
                using var image = SKImage.FromBitmap(subset);
                using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
                using var stream = System.IO.File.OpenWrite(destinationPath);
                data.SaveTo(stream);
            }
            else
            {
                using var image = SKImage.FromBitmap(original);
                using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
                using var stream = System.IO.File.OpenWrite(destinationPath);
                data.SaveTo(stream);
            }
        }

        private string PrettifyFilename(string filename)
        {
            return filename
                .Replace("/", "／")
                .Replace("\\", "＼")
                .Replace(":", "：")
                .Replace("*", "＊")
                .Replace("?", "？")
                .Replace("\"", "＂")
                .Replace("<", "＜")
                .Replace(">", "＞")
                .Replace("|", "｜")
                .Trim();
        }

        private class YtDlpInfo
        {
            public string? id { get; set; }
            public string? title { get; set; }
            public string? uploader { get; set; }
            public string? ext { get; set; }
            public double? duration { get; set; }
            public long? filesize { get; set; }
            public long? filesize_approx { get; set; }
            public double? asr { get; set; }
            public double? abr { get; set; }
            public double? tbr { get; set; }
            public int? width { get; set; }
            public int? height { get; set; }
            public string? thumbnail { get; set; }
            public string? playlist { get; set; }
            public string? playlist_title { get; set; }
            public int? channel_count { get; set; }
            public string[]? categories { get; set; }
            public string? upload_date { get; set; }
        }
    }
}
