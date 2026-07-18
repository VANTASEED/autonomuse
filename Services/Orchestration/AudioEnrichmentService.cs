using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Autonomuse.Domain.Entities;
using Autonomuse.Shared.Contracts;
using Autonomuse.Shared.Utilities;
using Microsoft.Extensions.Logging;

namespace Autonomuse.Services.Orchestration
{
    public class AudioEnrichmentService : IAudioEnrichmentService
    {
        private readonly IAudioService _audioService;
        private readonly IExternalToolService _toolService;
        private readonly ISettingsService _settingsService;
        private readonly HttpClient _httpClient;
        private readonly ILogger<AudioEnrichmentService> _logger;

        private const string AcoustIdApiKey = "2q6JP7xq7S"; //shared key
        private const string UserAgent = "Autonomuse/1.0.0 ( vantaseed@gmail.com )";
        
        // MusicBrainz rate limit is 1 request per second
        private static readonly SemaphoreSlim MusicBrainzSemaphore = new(1, 1);
        private DateTime _lastMusicBrainzCall = DateTime.MinValue;

        public AudioEnrichmentService(
            IAudioService audioService,
            IExternalToolService toolService,
            ISettingsService settingsService,
            HttpClient httpClient,
            ILogger<AudioEnrichmentService> logger)
        {
            _audioService = audioService;
            _toolService = toolService;
            _settingsService = settingsService;
            _httpClient = httpClient;
            _logger = logger;

            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        }

        public async Task<(bool Success, string Message)> EnrichAudioAsync(AudioRecord record)
        {
            if (record.EnrichmentStatus == 3 || record.EnrichmentStatus >= 10)
            {
                _logger.LogInformation("Enrichment is disabled/locked for audio: {Title} ({GUID}). Bypassing.", record.Title, record.GUID);
                return (true, "Enrichment disabled/locked for this track. Bypassed.");
            }

            try
            {
                _logger.LogInformation("Enriching audio: {Title} ({GUID})", record.Title, record.GUID);
                int initialStatus = record.EnrichmentStatus;

                // 0. MANDATORY: Backup original state BEFORE ANY CHANGES
                await EnsureBackupCreatedAsync(record);

                // 1. Identification (using stored fingerprint)
                if (string.IsNullOrEmpty(record.Fingerprint))
                {
                    _logger.LogWarning("Missing fingerprint for {Title}. Enrichment may be less accurate.", record.Title);
                    // Fallback: Generate if missing (per user request to "remove" it, I'll strictly use what's there or fail if they want strictness)
                    // But for robustness, I'll return failure if missing.
                    return (false, "No audio fingerprint found. Please re-ingest this file.");
                }

                // 2. AcoustID & MusicBrainz (Best effort)
                var metadata = new MusicBrainzMetadata
                {
                    Title = record.AlternativeTitle ?? record.Title,
                    Artist = record.Artist,
                    Album = record.Album,
                    Genre = record.Genre,
                    Year = record.Year
                };

                var mbId = await GetMusicBrainzIdAsync(record.Fingerprint, record.Duration ?? 0);
                bool mbSuccess = false;

                if (mbId != null)
                {
                    var mbMeta = await GetMusicBrainzMetadataAsync(mbId);
                    if (mbMeta != null)
                    {
                        mbSuccess = true;
                        
                        // Merge MusicBrainz data into our metadata object
                        metadata.Title = mbMeta.Title ?? metadata.Title;
                        metadata.Artist = mbMeta.Artist ?? metadata.Artist;
                        metadata.Album = mbMeta.Album ?? metadata.Album;
                        metadata.Genre = mbMeta.Genre ?? metadata.Genre;
                        metadata.Year = mbMeta.Year ?? metadata.Year;
                        metadata.ReleaseId = mbMeta.ReleaseId;
                        metadata.AppleMusicId = mbMeta.AppleMusicId;
                        
                        record.EnrichmentStatus = 1; // MusicBrainz Enriched
                    }
                }

                // 3. Apple Music Step
                bool amSuccess = await ApplyAppleMusicTranslationAsync(record, metadata);
                
                if (amSuccess)
                {
                    if (!mbSuccess)
                    {
                        record.EnrichmentStatus = 2;
                    }
                }
                bool changed = ApplyMetadata(record, metadata);
                
                // 4. Cover Art Step
                bool artFound = false;
                if (!string.IsNullOrEmpty(metadata.ReleaseId))
                {
                    artFound = await UpdateCoverArtArchiveAsync(record, metadata.ReleaseId);
                }

                // Fallback: If no MB art found, and we matched Apple Music, try to download Apple art
                if (!artFound && !string.IsNullOrEmpty(metadata.AppleMusicArtworkUrl))
                {
                    await DownloadAppleMusicCoverAsync(record, metadata.AppleMusicArtworkUrl);
                    artFound = true;
                }

                await _audioService.UpdateAudioRecordAsync(record);
                await _audioService.UpdatePhysicalTagsAsync(record);
                
                if (changed || artFound) return (true, "Metadata enriched successfully.");
                return (true, "No changes found.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Enrichment failed for {Title}", record.Title);
                return (false, $"Error: {ex.Message}");
            }
        }

        public async Task<(bool Success, string Message)> RestoreAudioAsync(AudioRecord record)
        {
            try
            {
                _logger.LogInformation("Restoring audio metadata for: {Title} ({GUID})", record.Title, record.GUID);

                var backup = await _audioService.GetAudioBackupAsync(record.GUID);
                if (backup == null) return (false, "No backup metadata found for this track.");

                var libraryPath = await _settingsService.GetSettingAsync("LibraryPath");
                
                // --- Cover Art Restoration Logic ---
                if (!string.IsNullOrEmpty(libraryPath))
                {
                    var standardArtPath = Path.Combine(libraryPath, "Art", "Audio", $"{record.GUID}.jpg");
                    var backupFolder = Path.Combine(libraryPath, "Art", "Backup");
                    var backupPath = Path.Combine(backupFolder, $"{record.GUID}.jpg");

                    if (string.IsNullOrEmpty(backup.CoverArtPath))
                    {
                        // Originally NO art. Just delete the file if it exists.
                        if (System.IO.File.Exists(standardArtPath)) System.IO.File.Delete(standardArtPath);
                        record.CoverArtPath = null;
                    }
                    else
                    {
                        // Originally HAD art. Copy from backup back to audio folder.
                        if (System.IO.File.Exists(backupPath))
                        {
                            if (!Directory.Exists(Path.GetDirectoryName(standardArtPath))) 
                                Directory.CreateDirectory(Path.GetDirectoryName(standardArtPath)!);

                            System.IO.File.Copy(backupPath, standardArtPath, true);
                            System.IO.File.Delete(backupPath);
                            record.CoverArtPath = standardArtPath;
                        }
                    }
                }

                record.AlternativeTitle = backup.AlternativeTitle;
                record.YoutubeID = backup.YoutubeID;
                record.Artist = backup.Artist;
                record.Album = backup.Album;
                record.Genre = backup.Genre;
                record.Year = backup.Year;
                record.EnrichmentStatus = 0;

                await _audioService.UpdateAudioRecordAsync(record);
                await _audioService.UpdatePhysicalTagsAsync(record);
                await _audioService.DeleteAudioBackupAsync(record.GUID);

                return (true, "Metadata restored successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Restoration failed for {Title}", record.Title);
                return (false, $"Error: {ex.Message}");
            }
        }

        public async Task EnrichAllAudioAsync(Action<string, double>? progressCallback = null)
        {
            var allAudio = await _audioService.GetAllAudioAsync();
            int total = allAudio.Count;
            int current = 0;

            foreach (var audio in allAudio)
            {
                if (audio.EnrichmentStatus == 1 || audio.EnrichmentStatus == 2 || audio.EnrichmentStatus == 3 || audio.EnrichmentStatus >= 10 || audio.EnrichmentStatus == 4)
                {
                    total--; // Skip enriched, manual, or disabled/locked in batch
                    continue;
                }

                current++;
                progressCallback?.Invoke($"Enriching {audio.Title}...", (double)current / total);
                await EnrichAudioAsync(audio);
            }
        }

        private async Task<string?> GetMusicBrainzIdAsync(string fingerprint, double duration)
        {
            var userKey = await _settingsService.GetSettingAsync("AcoustIdApiKey");
            var key = string.IsNullOrEmpty(userKey) ? AcoustIdApiKey : userKey;
            return await RetryAsync(async () =>
            {
                var url = $"https://api.acoustid.org/v2/lookup?client={key}&duration={(int)Math.Round(duration)}&fingerprint={fingerprint}&meta=recordingids";
                var response = await _httpClient.GetFromJsonAsync<AcoustIdResponse>(url);

                return response?.Results?
                    .FirstOrDefault(r => r.Recordings != null && r.Recordings.Any())?
                    .Recordings?
                    .FirstOrDefault()?.Id;
            });
        }

        private async Task<MusicBrainzMetadata?> GetMusicBrainzMetadataAsync(string mbId)
        {
            await MusicBrainzSemaphore.WaitAsync();
            try
            {
                // Respect rate limit
                var elapsed = DateTime.UtcNow - _lastMusicBrainzCall;
                if (elapsed < TimeSpan.FromSeconds(1))
                {
                    await Task.Delay(TimeSpan.FromSeconds(1) - elapsed);
                }

                var result = await RetryAsync(async () =>
                {
                    var url = $"https://musicbrainz.org/ws/2/recording/{mbId}?inc=artists+releases+genres+url-rels&fmt=json";
                    var response = await _httpClient.GetAsync(url);
                    response.EnsureSuccessStatusCode();
                    
                    var json = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    var metadata = new MusicBrainzMetadata();
                    
                    if (root.TryGetProperty("title", out var titleProp)) 
                        metadata.Title = titleProp.GetString();

                    if (root.TryGetProperty("artist-credit", out var artists) && artists.GetArrayLength() > 0)
                    {
                        var firstArtist = artists[0];
                        if (firstArtist.TryGetProperty("name", out var nameProp))
                            metadata.Artist = nameProp.GetString();
                    }

                    if (root.TryGetProperty("releases", out var releases) && releases.GetArrayLength() > 0)
                    {
                        var firstRelease = releases[0];
                        if (firstRelease.TryGetProperty("title", out var albumProp))
                            metadata.Album = albumProp.GetString();
                        
                        if (firstRelease.TryGetProperty("id", out var idProp))
                            metadata.ReleaseId = idProp.GetString();

                        if (firstRelease.TryGetProperty("date", out var dateProp))
                            metadata.Year = ParseYear(dateProp.GetString());
                    }

                    if (root.TryGetProperty("genres", out var genres) && genres.GetArrayLength() > 0)
                    {
                        if (genres[0].TryGetProperty("name", out var genreProp))
                            metadata.Genre = genreProp.GetString();
                    }

                    if (string.IsNullOrEmpty(metadata.Genre) && root.TryGetProperty("releases", out var relsForGenre) && relsForGenre.GetArrayLength() > 0)
                    {
                        var firstRel = relsForGenre[0];
                        if (firstRel.TryGetProperty("genres", out var relGenres) && relGenres.GetArrayLength() > 0)
                        {
                            if (relGenres[0].TryGetProperty("name", out var relGenreProp))
                                metadata.Genre = relGenreProp.GetString();
                        }
                    }

                    if (root.TryGetProperty("relations", out var relations))
                    {
                        foreach (var rel in relations.EnumerateArray())
                        {
                            if (rel.TryGetProperty("url", out var urlObj) && urlObj.TryGetProperty("resource", out var res))
                            {
                                var resUrl = res.GetString();
                                if (!string.IsNullOrEmpty(resUrl) && (resUrl.Contains("music.apple.com") || resUrl.Contains("itunes.apple.com")))
                                {
                                    metadata.AppleMusicId = ExtractAppleMusicId(resUrl);
                                    break;
                                }
                            }
                        }
                    }

                    if (string.IsNullOrEmpty(metadata.AppleMusicId) && !string.IsNullOrEmpty(metadata.ReleaseId))
                    {
                        var releaseMeta = await GetMusicBrainzReleaseMetadataAsync(metadata.ReleaseId);
                        if (releaseMeta != null && !string.IsNullOrEmpty(releaseMeta.AppleMusicId))
                        {
                            metadata.AppleMusicId = releaseMeta.AppleMusicId;
                        }
                    }

                    return metadata;
                });

                _lastMusicBrainzCall = DateTime.UtcNow;
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("MusicBrainz lookup failed for {Id}: {Error}", mbId, ex.Message);
                return null;
            }
            finally
            {
                MusicBrainzSemaphore.Release();
            }
        }

        private async Task<MusicBrainzMetadata?> GetMusicBrainzReleaseMetadataAsync(string releaseId)
        {
            try
            {
                var result = await RetryAsync(async () =>
                {
                    var url = $"https://musicbrainz.org/ws/2/release/{releaseId}?inc=url-rels&fmt=json";
                    var response = await _httpClient.GetAsync(url);
                    if (!response.IsSuccessStatusCode) return null;

                    var json = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    var metadata = new MusicBrainzMetadata();
                    if (root.TryGetProperty("relations", out var relations))
                    {
                        foreach (var rel in relations.EnumerateArray())
                        {
                            if (rel.TryGetProperty("url", out var urlObj) && urlObj.TryGetProperty("resource", out var res))
                            {
                                var resUrl = res.GetString();
                                if (!string.IsNullOrEmpty(resUrl) && (resUrl.Contains("music.apple.com") || resUrl.Contains("itunes.apple.com")))
                                {
                                    metadata.AppleMusicId = ExtractAppleMusicId(resUrl);
                                    break;
                                }
                            }
                        }
                    }
                    return metadata;
                });
                return result;
            }
            catch { return null; }
        }

        private static string? ExtractAppleMusicId(string url)
        {
            try
            {
                var uri = new Uri(url);
                var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
                var songId = query["i"];
                if (!string.IsNullOrEmpty(songId)) return songId;

                // Fallback: extract from path if it's an album link
                var parts = uri.AbsolutePath.Split('/');
                var lastPart = parts.Last();
                if (long.TryParse(lastPart, out _)) return lastPart;
            }
            catch { }
            return null;
        }

        private async Task<bool> ApplyAppleMusicTranslationAsync(AudioRecord record, MusicBrainzMetadata meta)
        {
            try
            {
                // Get sterilization tags
                var tagsStr = await _settingsService.GetSettingAsync("StringSterilization");
                var tags = tagsStr?.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();

                var searchTitle = FilePathSanitizer.SterilizeTitle(meta.Title ?? record.Title, tags);
                var searchArtist = meta.Artist ?? record.Artist;

                AppleMusicResult? result = null;
                
                // Priority: Use Direct ID from MusicBrainz if available
                if (!string.IsNullOrEmpty(meta.AppleMusicId))
                {
                    _logger.LogInformation("Attempting direct Apple Music lookup for ID: {Id}", meta.AppleMusicId);
                    var idResult = await LookupAppleMusicByIdAsync(meta.AppleMusicId);
                    
                    if (idResult != null)
                    {
                        // Store artwork from the direct ID match immediately
                        if (!string.IsNullOrEmpty(idResult.ArtworkUrl))
                        {
                            meta.AppleMusicArtworkUrl = await GetHighResArtworkUrlAsync(idResult.ArtworkUrl);
                            _logger.LogInformation("Preserved direct Apple Music artwork for ID: {Id}", meta.AppleMusicId);
                        }

                        // If it has track data, use it as our primary result
                        if (!string.IsNullOrEmpty(idResult.TrackName))
                        {
                            result = idResult;
                        }
                        else
                        {
                            _logger.LogInformation("ID {Id} returned no track data (likely an album ID). Proceeding to search for title.", meta.AppleMusicId);
                        }
                    }
                }

                // Search Step: If no direct track result found, or ID lookup failed entirely
                if (result == null)
                {
                    _logger.LogInformation("Attempting Apple Music search for: {Title} {Artist}", searchTitle, searchArtist);
                    result = await SearchAppleMusicAsync(searchTitle, searchArtist);
                    
                    if (result == null)
                    {
                        var cleanTitle = System.Text.RegularExpressions.Regex.Replace(searchTitle, @"[^a-zA-Z0-9\s]", " ");
                        var cleanArtist = !string.IsNullOrEmpty(searchArtist) 
                            ? System.Text.RegularExpressions.Regex.Replace(searchArtist, @"[^a-zA-Z0-9\s]", " ") 
                            : "";
                        
                        if (!string.IsNullOrWhiteSpace(cleanTitle))
                        {
                            _logger.LogInformation("Attempting fallback Apple Music search for: {Title} {Artist}", cleanTitle, cleanArtist);
                            result = await SearchAppleMusicAsync(cleanTitle, cleanArtist);
                        }
                    }
                }

                if (result != null)
                {
                    _logger.LogInformation("Apple Music translation found for {Title}: {NewTitle} by {NewArtist}", 
                        meta.Title, result.TrackName, result.ArtistName);
                    
                    // Update Translation
                    if (!string.IsNullOrEmpty(result.TrackName)) meta.Title = result.TrackName;
                    if (!string.IsNullOrEmpty(result.ArtistName)) meta.Artist = result.ArtistName;
                    if (!string.IsNullOrEmpty(result.PrimaryGenreName)) meta.Genre = result.PrimaryGenreName;

                    // Update Artwork if we don't already have one from a direct ID match
                    if (string.IsNullOrEmpty(meta.AppleMusicArtworkUrl) && !string.IsNullOrEmpty(result.ArtworkUrl))
                    {
                        meta.AppleMusicArtworkUrl = await GetHighResArtworkUrlAsync(result.ArtworkUrl);
                    }
                    
                    // Final check: if we still have a Release ID (meaning no MB art yet), download now
                    if (!string.IsNullOrEmpty(meta.AppleMusicArtworkUrl) && string.IsNullOrEmpty(meta.ReleaseId))
                    {
                        await DownloadAppleMusicCoverAsync(record, meta.AppleMusicArtworkUrl);
                    }
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Apple Music translation failed: {Error}", ex.Message);
            }
            return false;
        }

        private async Task DownloadAppleMusicCoverAsync(AudioRecord record, string url)
        {
            try
            {
                var libraryPath = await _settingsService.GetSettingAsync("LibraryPath");
                if (string.IsNullOrEmpty(libraryPath)) return;

                var artFolder = Path.Combine(libraryPath, "Art", "Audio");
                if (!Directory.Exists(artFolder)) Directory.CreateDirectory(artFolder);

                var fileName = $"{record.GUID}.jpg";
                var destinationPath = Path.Combine(artFolder, fileName);

                var response = await _httpClient.GetAsync(url);
                
                // Fallback for High Quality: if 1200px fails, try 500px
                if (!response.IsSuccessStatusCode && url.Contains("1200x1200bb.jpg"))
                {
                    _logger.LogInformation("1200px cover not found for {Title}, falling back to 500px", record.Title);
                    var fallbackUrl = url.Replace("1200x1200bb.jpg", "500x500bb.jpg");
                    response = await _httpClient.GetAsync(fallbackUrl);
                }

                if (response.IsSuccessStatusCode)
                {
                    var data = await response.Content.ReadAsByteArrayAsync();
                    
                    await File.WriteAllBytesAsync(destinationPath, data);
                    record.CoverArtPath = destinationPath;
                    _logger.LogInformation("Apple Music cover art saved for {Title}", record.Title);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to download Apple Music cover art");
            }
        }


        private async Task<string> GetHighResArtworkUrlAsync(string baseUrl)
        {
            var quality = await _settingsService.GetSettingAsync("CoverArtQuality") ?? "standard";
            if (quality == "high")
            {
                return baseUrl.Replace("100x100bb.jpg", "1200x1200bb.jpg");
            }
            return baseUrl.Replace("100x100bb.jpg", "500x500bb.jpg");
        }

        private async Task<AppleMusicResult?> LookupAppleMusicByIdAsync(string id)
        {
            try
            {
                // We use country=US as requested to get English results
                var url = $"https://itunes.apple.com/lookup?id={id}&country=US&entity=song";
                var response = await _httpClient.GetFromJsonAsync<AppleMusicResponse>(url);
                return response?.Results?.FirstOrDefault();
            }
            catch { return null; }
        }

        private async Task<AppleMusicResult?> SearchAppleMusicAsync(string title, string? artist)
        {
            try
            {
                var query = string.IsNullOrEmpty(artist) ? title : $"{title} {artist}";
                var term = Uri.EscapeDataString(query);
                var url = $"https://itunes.apple.com/search?term={term}&country=US&media=music&entity=song&limit=1";

                var response = await _httpClient.GetFromJsonAsync<AppleMusicResponse>(url);
                return response?.Results?.FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }



        private async Task<bool> UpdateCoverArtArchiveAsync(AudioRecord record, string releaseId)
        {
            try
            {
                var url = $"https://coverartarchive.org/release/{releaseId}";
                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode) return false;

                var json = await response.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(json);
                var frontImage = doc.RootElement.GetProperty("images").EnumerateArray()
                    .FirstOrDefault(i => i.GetProperty("front").GetBoolean());

                string? imageUrl = null;
                if (frontImage.ValueKind != JsonValueKind.Undefined)
                {
                    var quality = await _settingsService.GetSettingAsync("CoverArtQuality") ?? "standard";
                    
                    if (frontImage.TryGetProperty("thumbnails", out var thumbs))
                    {
                        if (quality == "high")
                        {
                            // Priority 1: Original image (usually handled by the "image" property below)
                            // But user said "original" first.
                            imageUrl = frontImage.GetProperty("image").GetString();

                            // Fallback if original is somehow empty (unlikely if property exists)
                            if (string.IsNullOrEmpty(imageUrl))
                            {
                                if (thumbs.TryGetProperty("500", out var p500)) imageUrl = p500.GetString();
                                else if (thumbs.TryGetProperty("1200", out var p1200)) imageUrl = p1200.GetString();
                            }
                        }
                        else
                        {
                            // Standard Quality logic
                            // Priority 1: 500px
                            if (thumbs.TryGetProperty("500", out var p500)) imageUrl = p500.GetString();
                            
                            // Priority 2: 1200px (fallback)
                            if (string.IsNullOrEmpty(imageUrl) && thumbs.TryGetProperty("1200", out var p1200)) imageUrl = p1200.GetString();
                            
                            // Rule: if target doesn't exist (500 or 1200), do not download original if standard is chosen?
                            // User: "if target doesnt exist, then do not download" for Standard.
                        }
                    }
                    else if (quality == "high")
                    {
                        // High Quality always tries original if thumbnails missing
                        imageUrl = frontImage.GetProperty("image").GetString();
                    }
                }

                if (string.IsNullOrEmpty(imageUrl)) return false;

                var imageBytes = await _httpClient.GetByteArrayAsync(imageUrl);
                
                // Save to the same path, overwriting it
                if (!string.IsNullOrEmpty(record.CoverArtPath))
                {
                    await System.IO.File.WriteAllBytesAsync(record.CoverArtPath, imageBytes);
                    return true;
                }
                else
                {
                    // Create a new path if none exists
                    var libraryPath = await _settingsService.GetSettingAsync("LibraryPath");
                    if (string.IsNullOrEmpty(libraryPath)) return false;

                    var artFolder = Path.Combine(libraryPath, "Art", "Audio");
                    if (!Directory.Exists(artFolder)) Directory.CreateDirectory(artFolder);
                    
                    var artPath = Path.Combine(artFolder, $"{record.GUID}.jpg");
                    await System.IO.File.WriteAllBytesAsync(artPath, imageBytes);
                    record.CoverArtPath = artPath;
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Cover art update failed for {Title}: {Error}", record.Title, ex.Message);
                return false;
            }
        }

        private async Task EnsureBackupCreatedAsync(AudioRecord record)
        {
            try
            {
                // We strictly rely on the existence of the backup record now.
                // This ensures we always have an original state to return to.
                var existing = await _audioService.GetAudioBackupAsync(record.GUID);
                if (existing != null) return;

                _logger.LogInformation("Creating factory-original backup for {Title}", record.Title);

                // 1. Backup Metadata
                await BackupOriginalDataAsync(record);

                // 2. Backup Cover Art
                await BackupOriginalCoverArtAsync(record);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("EnsureBackupCreatedAsync failed: {Message}", ex.Message);
            }
        }

        private async Task BackupOriginalDataAsync(AudioRecord record)
        {
            var backup = new AudioBackup
            {
                GUID = record.GUID,
                AlternativeTitle = record.AlternativeTitle,
                YoutubeID = record.YoutubeID,
                Artist = record.Artist,
                Album = record.Album,
                Genre = record.Genre,
                Year = record.Year,
                CoverArtPath = record.CoverArtPath,
                CreatedAt = DateTime.UtcNow
            };
            await _audioService.CreateAudioBackupAsync(backup);
        }

        private async Task BackupOriginalCoverArtAsync(AudioRecord record)
        {
            if (!string.IsNullOrEmpty(record.CoverArtPath) && System.IO.File.Exists(record.CoverArtPath))
            {
                try
                {
                    var libraryPath = await _settingsService.GetSettingAsync("LibraryPath");
                    if (!string.IsNullOrEmpty(libraryPath))
                    {
                        var backupFolder = Path.Combine(libraryPath, "Art", "Backup");
                        if (!Directory.Exists(backupFolder)) Directory.CreateDirectory(backupFolder);
                        
                        var backupPath = Path.Combine(backupFolder, $"{record.GUID}.jpg");
                        
                        if (!System.IO.File.Exists(backupPath))
                        {
                            System.IO.File.Copy(record.CoverArtPath, backupPath);
                            _logger.LogInformation("Backed up original cover art to {Path}", backupPath);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Failed to backup cover art for {Title}: {Message}", record.Title, ex.Message);
                }
            }
        }

        private static bool ApplyMetadata(AudioRecord record, MusicBrainzMetadata meta)
        {
            bool changed = false;

            if (record.AlternativeTitle != meta.Title) { record.AlternativeTitle = meta.Title; changed = true; }
            if (record.Artist != meta.Artist) { record.Artist = meta.Artist; changed = true; }
            if (record.Album != meta.Album) { record.Album = meta.Album; changed = true; }
            if (record.Genre != meta.Genre && !string.IsNullOrEmpty(meta.Genre)) { record.Genre = meta.Genre; changed = true; }
            if (record.Year != meta.Year && meta.Year > 0) { record.Year = meta.Year; changed = true; }

            return changed;
        }

        private static int? ParseYear(string? dateStr)
        {
            if (string.IsNullOrEmpty(dateStr)) return null;
            if (DateTime.TryParse(dateStr, out var date)) return date.Year;
            if (int.TryParse(dateStr.Split('-')[0], out var year)) return year;
            return null;
        }

        private static async Task<T> RetryAsync<T>(Func<Task<T>> action, int maxRetries = 3)
        {
            Exception? last = null;
            for (int i = 0; i < maxRetries; i++)
            {
                try { return await action(); }
                catch (Exception ex) { last = ex; await Task.Delay(1000 * (i + 1)); }
            }
            throw last ?? new InvalidOperationException("Retry failed");
        }

        // --- Response Models ---

        private class AcoustIdResponse
        {
            [JsonPropertyName("status")] public string Status { get; set; } = "";
            [JsonPropertyName("results")] public List<AcoustIdResult>? Results { get; set; }
        }

        private class AcoustIdResult
        {
            [JsonPropertyName("id")] public string Id { get; set; } = "";
            [JsonPropertyName("recordings")] public List<AcoustIdRecording>? Recordings { get; set; }
        }

        private class AcoustIdRecording
        {
            [JsonPropertyName("id")] public string Id { get; set; } = "";
        }

        private class MusicBrainzMetadata
        {
            public string? Title { get; set; }
            public string? Artist { get; set; }
            public string? Album { get; set; }
            public string? Genre { get; set; }
            public int? Year { get; set; }
            public string? ReleaseId { get; set; }
            public string? AppleMusicId { get; set; }
            public string? AppleMusicArtworkUrl { get; set; }
        }

        private class AppleMusicResponse
        {
            [JsonPropertyName("resultCount")] public int ResultCount { get; set; }
            [JsonPropertyName("results")] public List<AppleMusicResult>? Results { get; set; }
        }

        private class AppleMusicResult
        {
            [JsonPropertyName("trackName")] public string? TrackName { get; set; }
            [JsonPropertyName("artistName")] public string? ArtistName { get; set; }
            [JsonPropertyName("artworkUrl100")] public string? ArtworkUrl { get; set; }
            [JsonPropertyName("releaseDate")] public string? ReleaseDate { get; set; }
            [JsonPropertyName("primaryGenreName")] public string? PrimaryGenreName { get; set; }
        }
    }
}
