using Autonomuse.Domain.Entities;
using Autonomuse.Infrastructure.Data;
using Autonomuse.Shared.Contracts;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using TagLib;

namespace Autonomuse.Services.Orchestration
{
    public class AudioService : IAudioService
    {
        private readonly MediaDatabaseService _mediaDb;
        private readonly ISettingsService _settingsService;
        private readonly IExternalToolService _toolService;
        private readonly ILogger<AudioService> _logger;

        private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp3", ".aac", ".wav", ".flac"
        };

        public AudioService(
            MediaDatabaseService mediaDb,
            ISettingsService settingsService,
            IExternalToolService toolService,
            ILogger<AudioService> logger)
        {
            _mediaDb = mediaDb;
            _settingsService = settingsService;
            _toolService = toolService;
            _logger = logger;
        }

        public async Task<AudioRecord> AddAudioAsync(string sourceFilePath, string source)
        {
            var fileName = Path.GetFileName(sourceFilePath);
            var extension = Path.GetExtension(sourceFilePath).ToLowerInvariant();
            var title = Path.GetFileNameWithoutExtension(sourceFilePath);

            if (!SupportedExtensions.Contains(extension))
            {
                throw new InvalidOperationException($"Unsupported audio format: {extension}. Supported: MP3, AAC, WAV, FLAC");
            }

            // Get library path to determine destination
            var libraryPath = await _settingsService.GetSettingAsync("LibraryPath");
            if (string.IsNullOrEmpty(libraryPath))
            {
                throw new InvalidOperationException("Library path is not configured. Please set it in Settings.");
            }

            // Ensure Audio subdirectory exists
            var audioFolder = Path.Combine(libraryPath, "Audio");
            if (!Directory.Exists(audioFolder))
            {
                Directory.CreateDirectory(audioFolder);
            }

            // Copy file to library
            var destinationPath = Path.Combine(audioFolder, fileName);

            // If file already exists, append GUID segment to avoid overwrite
            if (System.IO.File.Exists(destinationPath))
            {
                var guidSegment = Guid.NewGuid().ToString()[..8];
                destinationPath = Path.Combine(audioFolder, $"{title}_{guidSegment}{extension}");
                fileName = Path.GetFileName(destinationPath);
                title = Path.GetFileNameWithoutExtension(destinationPath);
            }

            System.IO.File.Copy(sourceFilePath, destinationPath, false);

            // Get file size
            var fileInfo = new FileInfo(destinationPath);

            var record = new AudioRecord
            {
                GUID = Guid.NewGuid().ToString(),
                FileName = fileName,
                Title = title,
                Extension = extension,
                AlternativeTitle = title,
                Source = source,
                FilePath = destinationPath,
                FileSize = fileInfo.Length,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            // Extract Metadata using TagLib
            try
            {
                using (var tfile = TagLib.File.Create(destinationPath))
                {
                    if (tfile.Properties != null)
                    {
                        record.Duration = tfile.Properties.Duration.TotalSeconds;
                        record.Bitrate = tfile.Properties.AudioBitrate;
                        record.SampleRate = tfile.Properties.AudioSampleRate;
                        record.Channels = tfile.Properties.AudioChannels;
                    }
                    if (tfile.Tag != null)
                    {
                        if (!string.IsNullOrEmpty(tfile.Tag.Title)) 
                        {
                            record.Title = tfile.Tag.Title;
                            record.AlternativeTitle = record.Title;
                        }
                        if (tfile.Tag.Performers?.Length > 0) record.Artist = string.Join(", ", tfile.Tag.Performers);
                        if (!string.IsNullOrEmpty(tfile.Tag.Album)) record.Album = tfile.Tag.Album;
                        if (tfile.Tag.Genres?.Length > 0) record.Genre = string.Join(", ", tfile.Tag.Genres);
                        if (tfile.Tag.Year > 0) record.Year = (int)tfile.Tag.Year;

                        // Extract Embedded Cover Art
                        if (tfile.Tag.Pictures != null && tfile.Tag.Pictures.Length > 0)
                        {
                            try
                            {
                                var libraryPathForArt = await _settingsService.GetSettingAsync("LibraryPath");
                                if (string.IsNullOrEmpty(libraryPathForArt)) return record;

                                var artFolder = Path.Combine(libraryPathForArt, "Art", "Audio");
                                if (!Directory.Exists(artFolder)) Directory.CreateDirectory(artFolder);
                                
                                var pic = tfile.Tag.Pictures[0];
                                var artPath = Path.Combine(artFolder, $"{record.GUID}.jpg");
                                
                                using var ms = new MemoryStream(pic.Data.Data);
                                using var original = SKBitmap.Decode(ms);
                                if (original != null)
                                {
                                    int size = Math.Min(original.Width, original.Height);
                                    int x = (original.Width - size) / 2;
                                    int y = (original.Height - size) / 2;

                                    using var subset = new SKBitmap(size, size);
                                    original.ExtractSubset(subset, new SKRectI(x, y, x + size, y + size));
                                    
                                    using var image = SKImage.FromBitmap(subset);
                                    using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
                                    using var stream = System.IO.File.OpenWrite(artPath);
                                    data.SaveTo(stream);
                                    
                                    record.CoverArtPath = artPath;
                                }
                            }
                            catch (Exception ex) { _logger.LogWarning("Failed to extract embedded art: {Ex}", ex.Message); }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to extract metadata for {File}: {Error}", destinationPath, ex.Message);
            }

            // Generate Fingerprint
            record.Fingerprint = await GenerateFingerprintAsync(record.FilePath);

            // Insert into database
            await AddAudioRecordAsync(record);

            _logger.LogInformation("Added audio: {Title} ({Extension}) from {Source}", record.Title, record.Extension, record.Source);
            return record;
        }

        public async Task<List<AudioRecord>> GetAllAudioAsync()
        {
            var records = new List<AudioRecord>();

            try
            {
                using var connection = new SqliteConnection(_mediaDb.GetConnectionString());
                await connection.OpenAsync();

                var command = connection.CreateCommand();
                command.CommandText = @"
                    SELECT [GUID], [FileName], [Title], [Extension], [AlternativeTitle], [Source], [YoutubeID],
                           [FilePath], [Artist], [Album], [Genre], [Duration], [Bitrate], 
                           [SampleRate], [Channels], [Year], [FileSize], [CoverArtPath], [Fingerprint], [CreatedAt], [UpdatedAt],
                           [EnrichmentStatus]
                    FROM Audio ORDER BY [CreatedAt] DESC;
                ";

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    records.Add(new AudioRecord
                    {
                        GUID = reader.GetString(0),
                        FileName = reader.GetString(1),
                        Title = reader.GetString(2),
                        Extension = reader.GetString(3),
                        AlternativeTitle = reader.IsDBNull(4) ? null : reader.GetString(4),
                        Source = reader.GetString(5),
                        YoutubeID = reader.IsDBNull(6) ? null : reader.GetString(6),
                        FilePath = reader.GetString(7),
                        Artist = reader.IsDBNull(8) ? null : reader.GetString(8),
                        Album = reader.IsDBNull(9) ? null : reader.GetString(9),
                        Genre = reader.IsDBNull(10) ? null : reader.GetString(10),
                        Duration = reader.IsDBNull(11) ? null : reader.GetDouble(11),
                        Bitrate = reader.IsDBNull(12) ? null : reader.GetInt32(12),
                        SampleRate = reader.IsDBNull(13) ? null : reader.GetInt32(13),
                        Channels = reader.IsDBNull(14) ? null : reader.GetInt32(14),
                        Year = reader.IsDBNull(15) ? null : reader.GetInt32(15),
                        FileSize = reader.IsDBNull(16) ? null : reader.GetInt64(16),
                        CoverArtPath = reader.IsDBNull(17) ? null : reader.GetString(17),
                        Fingerprint = reader.IsDBNull(18) ? null : reader.GetString(18),
                        CreatedAt = reader.GetDateTime(19),
                        UpdatedAt = reader.GetDateTime(20),
                        EnrichmentStatus = reader.GetInt32(21)
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving audio records");
            }

            return records;
        }

        public async Task AddAudioRecordAsync(AudioRecord record)
        {
            try
            {
                using var connection = new SqliteConnection(_mediaDb.GetConnectionString());
                await connection.OpenAsync();

                var command = connection.CreateCommand();
                command.CommandText = @"
                    INSERT INTO Audio (
                        [GUID], [FileName], [Title], [Extension], [AlternativeTitle], [Source], [YoutubeID],
                        [FilePath], [Artist], [Album], [Genre], [Duration], [Bitrate],
                        [SampleRate], [Channels], [Year], [FileSize], [CoverArtPath], [Fingerprint], [CreatedAt], [UpdatedAt],
                        [EnrichmentStatus]
                    ) VALUES (
                        @guid, @fileName, @title, @ext, @altTitle, @source, @yid,
                        @filePath, @artist, @album, @genre, @duration, @bitrate,
                        @sampleRate, @channels, @year, @fileSize, @coverArt, @fingerprint, @createdAt, @updatedAt,
                        @enrichStatus
                    );
                ";

                command.Parameters.AddWithValue("@guid", record.GUID);
                command.Parameters.AddWithValue("@fileName", record.FileName);
                command.Parameters.AddWithValue("@title", record.Title);
                command.Parameters.AddWithValue("@ext", record.Extension);
                command.Parameters.AddWithValue("@altTitle", (object?)record.AlternativeTitle ?? DBNull.Value);
                command.Parameters.AddWithValue("@source", record.Source);
                command.Parameters.AddWithValue("@yid", (object?)record.YoutubeID ?? DBNull.Value);
                command.Parameters.AddWithValue("@filePath", record.FilePath);
                command.Parameters.AddWithValue("@artist", (object?)record.Artist ?? DBNull.Value);
                command.Parameters.AddWithValue("@album", (object?)record.Album ?? DBNull.Value);
                command.Parameters.AddWithValue("@genre", (object?)record.Genre ?? DBNull.Value);
                command.Parameters.AddWithValue("@duration", (object?)record.Duration ?? DBNull.Value);
                command.Parameters.AddWithValue("@bitrate", (object?)record.Bitrate ?? DBNull.Value);
                command.Parameters.AddWithValue("@sampleRate", (object?)record.SampleRate ?? DBNull.Value);
                command.Parameters.AddWithValue("@channels", (object?)record.Channels ?? DBNull.Value);
                command.Parameters.AddWithValue("@year", (object?)record.Year ?? DBNull.Value);
                command.Parameters.AddWithValue("@fileSize", (object?)record.FileSize ?? DBNull.Value);
                command.Parameters.AddWithValue("@coverArt", (object?)record.CoverArtPath ?? DBNull.Value);
                command.Parameters.AddWithValue("@fingerprint", (object?)record.Fingerprint ?? DBNull.Value);
                command.Parameters.AddWithValue("@createdAt", record.CreatedAt);
                command.Parameters.AddWithValue("@updatedAt", record.UpdatedAt);
                command.Parameters.AddWithValue("@enrichStatus", record.EnrichmentStatus);

                await command.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error inserting audio record {Title}", record.Title);
                throw;
            }
        }

        public async Task<List<MediaPlaylist>> GetPlaylistsAsync()
        {
            var playlists = new List<MediaPlaylist>();
            try
            {
                using var connection = new SqliteConnection(_mediaDb.GetConnectionString());
                await connection.OpenAsync();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT [GUID], [Name], [Description], [CreatedAt], [UpdatedAt] FROM AudioPlaylist ORDER BY [Name]";
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    playlists.Add(new MediaPlaylist
                    {
                        GUID = reader.GetString(0),
                        Name = reader.GetString(1),
                        Description = reader.IsDBNull(2) ? null : reader.GetString(2),
                        CreatedAt = reader.GetDateTime(3),
                        UpdatedAt = reader.GetDateTime(4)
                    });
                }
            }
            catch (Exception ex) { _logger.LogError(ex, "Error getting audio playlists"); }
            return playlists;
        }

        public async Task<MediaPlaylist> CreatePlaylistAsync(string name, string? description = null)
        {
            var existingPlaylists = await GetPlaylistsAsync();
            var existing = existingPlaylists.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            
            if (existing != null)
            {
                if (string.IsNullOrEmpty(existing.Description) && !string.IsNullOrEmpty(description))
                {
                    await UpdatePlaylistDescriptionAsync(existing.GUID, description);
                    existing.Description = description;
                }
                return existing;
            }

            var playlist = new MediaPlaylist { GUID = Guid.NewGuid().ToString(), Name = name, Description = description };
            using var connection = new SqliteConnection(_mediaDb.GetConnectionString());
            await connection.OpenAsync();
            var cmd = connection.CreateCommand();
            cmd.CommandText = "INSERT INTO AudioPlaylist ([GUID], [Name], [Description], [CreatedAt], [UpdatedAt]) VALUES (@g, @n, @d, @c, @u)";
            cmd.Parameters.AddWithValue("@g", playlist.GUID);
            cmd.Parameters.AddWithValue("@n", playlist.Name);
            cmd.Parameters.AddWithValue("@d", (object?)playlist.Description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@c", playlist.CreatedAt);
            cmd.Parameters.AddWithValue("@u", playlist.UpdatedAt);
            await cmd.ExecuteNonQueryAsync();
            _logger.LogInformation("Created audio playlist: {Name}", name);
            return playlist;
        }

        public async Task UpdatePlaylistDescriptionAsync(string guid, string description)
        {
            using var connection = new SqliteConnection(_mediaDb.GetConnectionString());
            await connection.OpenAsync();
            var cmd = connection.CreateCommand();
            cmd.CommandText = "UPDATE AudioPlaylist SET [Description] = @d, [UpdatedAt] = @u WHERE [GUID] = @g";
            cmd.Parameters.AddWithValue("@d", description);
            cmd.Parameters.AddWithValue("@u", DateTime.UtcNow);
            cmd.Parameters.AddWithValue("@g", guid);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task AddToPlaylistAsync(string playlistGuid, string audioGuid)
        {
            using var connection = new SqliteConnection(_mediaDb.GetConnectionString());
            await connection.OpenAsync();
            // Get next order index
            var countCmd = connection.CreateCommand();
            countCmd.CommandText = "SELECT COALESCE(MAX([OrderIndex]), -1) + 1 FROM AudioPlaylistItems WHERE [PlaylistGUID] = @pg";
            countCmd.Parameters.AddWithValue("@pg", playlistGuid);
            var nextIndex = Convert.ToInt32(await countCmd.ExecuteScalarAsync());

            var cmd = connection.CreateCommand();
            cmd.CommandText = "INSERT INTO AudioPlaylistItems ([GUID], [PlaylistGUID], [AudioGUID], [OrderIndex], [AddedAt]) VALUES (@g, @pg, @ag, @oi, @a)";
            cmd.Parameters.AddWithValue("@g", Guid.NewGuid().ToString());
            cmd.Parameters.AddWithValue("@pg", playlistGuid);
            cmd.Parameters.AddWithValue("@ag", audioGuid);
            cmd.Parameters.AddWithValue("@oi", nextIndex);
            cmd.Parameters.AddWithValue("@a", DateTime.UtcNow);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<AudioRecord?> GetAudioByTitleAndSourceAsync(string title, string source)
        {
            try
            {
                using var connection = new SqliteConnection(_mediaDb.GetConnectionString());
                await connection.OpenAsync();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT [GUID], [FileName], [Title], [Extension], [AlternativeTitle], [Source], [YoutubeID], [FilePath], [Artist], [Album], [Genre], [Duration], [Bitrate], [SampleRate], [Channels], [Year], [FileSize], [CoverArtPath], [CreatedAt], [UpdatedAt] FROM Audio WHERE [Title] = @title AND [Source] = @source COLLATE NOCASE LIMIT 1";
                cmd.Parameters.AddWithValue("@title", title);
                cmd.Parameters.AddWithValue("@source", source);

                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return new AudioRecord
                    {
                        GUID = reader.GetString(0),
                        FileName = reader.GetString(1),
                        Title = reader.GetString(2),
                        Extension = reader.GetString(3),
                        AlternativeTitle = reader.IsDBNull(4) ? null : reader.GetString(4),
                        Source = reader.GetString(5),
                        YoutubeID = reader.IsDBNull(6) ? null : reader.GetString(6),
                        FilePath = reader.GetString(7),
                        Artist = reader.IsDBNull(8) ? null : reader.GetString(8),
                        Album = reader.IsDBNull(9) ? null : reader.GetString(9),
                        Genre = reader.IsDBNull(10) ? null : reader.GetString(10),
                        Duration = reader.IsDBNull(11) ? null : reader.GetDouble(11),
                        Bitrate = reader.IsDBNull(12) ? null : reader.GetInt32(12),
                        SampleRate = reader.IsDBNull(13) ? null : reader.GetInt32(13),
                        Channels = reader.IsDBNull(14) ? null : reader.GetInt32(14),
                        Year = reader.IsDBNull(15) ? null : reader.GetInt32(15),
                        FileSize = reader.IsDBNull(16) ? null : reader.GetInt64(16),
                        CoverArtPath = reader.IsDBNull(17) ? null : reader.GetString(17),
                        CreatedAt = reader.GetDateTime(18),
                        UpdatedAt = reader.GetDateTime(19)
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting audio by title {Title}", title);
            }
            return null;
        }

        public async Task<AudioRecord?> GetAudioByYoutubeIDAsync(string youtubeId)
        {
            try
            {
                using var connection = new SqliteConnection(_mediaDb.GetConnectionString());
                await connection.OpenAsync();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT [GUID], [FileName], [Title], [Extension], [AlternativeTitle], [Source], [YoutubeID], [FilePath], [Artist], [Album], [Genre], [Duration], [Bitrate], [SampleRate], [Channels], [Year], [FileSize], [CoverArtPath], [CreatedAt], [UpdatedAt] FROM Audio WHERE [YoutubeID] = @yid LIMIT 1";
                cmd.Parameters.AddWithValue("@yid", youtubeId);

                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return new AudioRecord
                    {
                        GUID = reader.GetString(0),
                        FileName = reader.GetString(1),
                        Title = reader.GetString(2),
                        Extension = reader.GetString(3),
                        AlternativeTitle = reader.IsDBNull(4) ? null : reader.GetString(4),
                        Source = reader.GetString(5),
                        YoutubeID = reader.IsDBNull(6) ? null : reader.GetString(6),
                        FilePath = reader.GetString(7),
                        Artist = reader.IsDBNull(8) ? null : reader.GetString(8),
                        Album = reader.IsDBNull(9) ? null : reader.GetString(9),
                        Genre = reader.IsDBNull(10) ? null : reader.GetString(10),
                        Duration = reader.IsDBNull(11) ? null : reader.GetDouble(11),
                        Bitrate = reader.IsDBNull(12) ? null : reader.GetInt32(12),
                        SampleRate = reader.IsDBNull(13) ? null : reader.GetInt32(13),
                        Channels = reader.IsDBNull(14) ? null : reader.GetInt32(14),
                        Year = reader.IsDBNull(15) ? null : reader.GetInt32(15),
                        FileSize = reader.IsDBNull(16) ? null : reader.GetInt64(16),
                        CoverArtPath = reader.IsDBNull(17) ? null : reader.GetString(17),
                        CreatedAt = reader.GetDateTime(18),
                        UpdatedAt = reader.GetDateTime(19)
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting audio by YoutubeID {Id}", youtubeId);
            }
            return null;
        }

        public async Task<List<string>> GetPlaylistsForAudioAsync(string audioGuid)
        {
            var playlistGuids = new List<string>();
            try
            {
                using var connection = new SqliteConnection(_mediaDb.GetConnectionString());
                await connection.OpenAsync();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT [PlaylistGUID] FROM AudioPlaylistItems WHERE [AudioGUID] = @ag";
                cmd.Parameters.AddWithValue("@ag", audioGuid);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    playlistGuids.Add(reader.GetString(0));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting playlists for audio {Guid}", audioGuid);
            }
            return playlistGuids;
        }
        public async Task UpdateAudioRecordAsync(AudioRecord record)
        {
            try
            {
                using var connection = new SqliteConnection(_mediaDb.GetConnectionString());
                await connection.OpenAsync();

                var command = connection.CreateCommand();
                command.CommandText = @"
                    UPDATE Audio SET 
                        [Title] = @title, [AlternativeTitle] = @altTitle, [Artist] = @artist, 
                        [Album] = @album, [Genre] = @genre, [Year] = @year, 
                        [CoverArtPath] = @coverArt, [Fingerprint] = @fingerprint, [UpdatedAt] = @updatedAt,
                        [EnrichmentStatus] = @enrichStatus
                    WHERE [GUID] = @guid;
                ";

                command.Parameters.AddWithValue("@title", record.Title);
                command.Parameters.AddWithValue("@altTitle", (object?)record.AlternativeTitle ?? DBNull.Value);
                command.Parameters.AddWithValue("@artist", (object?)record.Artist ?? DBNull.Value);
                command.Parameters.AddWithValue("@album", (object?)record.Album ?? DBNull.Value);
                command.Parameters.AddWithValue("@genre", (object?)record.Genre ?? DBNull.Value);
                command.Parameters.AddWithValue("@year", (object?)record.Year ?? DBNull.Value);
                command.Parameters.AddWithValue("@coverArt", (object?)record.CoverArtPath ?? DBNull.Value);
                command.Parameters.AddWithValue("@fingerprint", (object?)record.Fingerprint ?? DBNull.Value);
                command.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow);
                command.Parameters.AddWithValue("@enrichStatus", record.EnrichmentStatus);
                command.Parameters.AddWithValue("@guid", record.GUID);

                await command.ExecuteNonQueryAsync();
                _logger.LogInformation("Updated audio record: {Title} ({GUID})", record.Title, record.GUID);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating audio record {Title}", record.Title);
                throw;
            }
        }

        public async Task UpdatePhysicalTagsAsync(AudioRecord record)
        {
            try
            {
                if (!System.IO.File.Exists(record.FilePath))
                {
                    _logger.LogWarning("Cannot update tags: file not found at {Path}", record.FilePath);
                    return;
                }

                // TagLib operations are synchronous
                await Task.Run(() =>
                {
                    using var tfile = TagLib.File.Create(record.FilePath);
                    
                    // For MP3 files, we explicitly handle ID3v2.3 for maximum Windows compatibility
                    if (record.FilePath.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
                    {
                        // Ensure we have an ID3v2 tag
                        var id3v2 = tfile.GetTag(TagLib.TagTypes.Id3v2, true) as TagLib.Id3v2.Tag;
                        if (id3v2 != null)
                        {
                            // Remove ID3v1 to avoid conflicts
                            tfile.RemoveTags(TagLib.TagTypes.Id3v1); 
                        }
                    }

                    tfile.Tag.Title = record.AlternativeTitle ?? record.Title;
                    if (!string.IsNullOrEmpty(record.Artist))
                    {
                        tfile.Tag.Performers = new[] { record.Artist };
                    }
                    if (!string.IsNullOrEmpty(record.Album))
                    {
                        tfile.Tag.Album = record.Album;
                    }
                    if (record.Year.HasValue && record.Year > 0)
                    {
                        tfile.Tag.Year = (uint)record.Year.Value;
                    }
                    if (!string.IsNullOrEmpty(record.Genre))
                    {
                        tfile.Tag.Genres = new[] { record.Genre };
                    }

                    // Embed Cover Art if available
                    if (!string.IsNullOrEmpty(record.CoverArtPath) && System.IO.File.Exists(record.CoverArtPath))
                    {
                        try
                        {
                            var pic = new TagLib.Picture(record.CoverArtPath)
                            {
                                Type = TagLib.PictureType.FrontCover,
                                Description = "Cover"
                            };
                            tfile.Tag.Pictures = new[] { pic };
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning("Could not embed cover art into file: {Error}", ex.Message);
                        }
                    }

                    tfile.Save();
                });

                _logger.LogInformation("Successfully updated physical tags for {Title}", record.Title);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating physical tags for {Title}", record.Title);
            }
        }

        public async Task CreateAudioBackupAsync(AudioBackup backup)
        {
            try
            {
                using var connection = new SqliteConnection(_mediaDb.GetConnectionString());
                await connection.OpenAsync();

                var command = connection.CreateCommand();
                command.CommandText = @"
                    INSERT OR IGNORE INTO AudioBackup (
                        GUID, AlternativeTitle, YoutubeID, Artist, Album, Genre, Year, CoverArtPath, CreatedAt
                    ) VALUES (
                        @guid, @altTitle, @ytId, @artist, @album, @genre, @year, @coverArt, @createdAt
                    );";

                command.Parameters.AddWithValue("@guid", backup.GUID);
                command.Parameters.AddWithValue("@altTitle", (object?)backup.AlternativeTitle ?? DBNull.Value);
                command.Parameters.AddWithValue("@ytId", (object?)backup.YoutubeID ?? DBNull.Value);
                command.Parameters.AddWithValue("@artist", (object?)backup.Artist ?? DBNull.Value);
                command.Parameters.AddWithValue("@album", (object?)backup.Album ?? DBNull.Value);
                command.Parameters.AddWithValue("@genre", (object?)backup.Genre ?? DBNull.Value);
                command.Parameters.AddWithValue("@year", (object?)backup.Year ?? DBNull.Value);
                command.Parameters.AddWithValue("@coverArt", (object?)backup.CoverArtPath ?? DBNull.Value);
                command.Parameters.AddWithValue("@createdAt", backup.CreatedAt);

                await command.ExecuteNonQueryAsync();
                _logger.LogInformation("Created metadata backup for: {GUID}", backup.GUID);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating audio backup for {GUID}", backup.GUID);
                throw;
            }
        }

        public async Task<AudioBackup?> GetAudioBackupAsync(string guid)
        {
            try
            {
                using var connection = new SqliteConnection(_mediaDb.GetConnectionString());
                await connection.OpenAsync();

                var command = connection.CreateCommand();
                command.CommandText = "SELECT * FROM AudioBackup WHERE GUID = @guid";
                command.Parameters.AddWithValue("@guid", guid);

                using var reader = await command.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return new AudioBackup
                    {
                        GUID = reader.GetString(0),
                        AlternativeTitle = reader.IsDBNull(1) ? null : reader.GetString(1),
                        YoutubeID = reader.IsDBNull(2) ? null : reader.GetString(2),
                        Artist = reader.IsDBNull(3) ? null : reader.GetString(3),
                        Album = reader.IsDBNull(4) ? null : reader.GetString(4),
                        Genre = reader.IsDBNull(5) ? null : reader.GetString(5),
                        Year = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                        CoverArtPath = reader.IsDBNull(7) ? null : reader.GetString(7),
                        CreatedAt = reader.GetDateTime(8)
                    };
                }
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting audio backup for {GUID}", guid);
                return null;
            }
        }

        public async Task DeleteAudioBackupAsync(string guid)
        {
            try
            {
                using var connection = new SqliteConnection(_mediaDb.GetConnectionString());
                await connection.OpenAsync();

                var command = connection.CreateCommand();
                command.CommandText = "DELETE FROM AudioBackup WHERE GUID = @guid";
                command.Parameters.AddWithValue("@guid", guid);

                await command.ExecuteNonQueryAsync();
                _logger.LogInformation("Deleted audio backup for {GUID}", guid);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting audio backup for {GUID}", guid);
            }
        }

        public async Task<List<AudioRecord>> GetAudioInPlaylistAsync(string playlistGuid)
        {
            var records = new List<AudioRecord>();
            try
            {
                using var connection = new SqliteConnection(_mediaDb.GetConnectionString());
                await connection.OpenAsync();
                var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    SELECT a.[GUID], a.[FileName], a.[Title], a.[Extension], a.[AlternativeTitle], a.[Source], a.[YoutubeID],
                           a.[FilePath], a.[Artist], a.[Album], a.[Genre], a.[Duration], a.[Bitrate], 
                           a.[SampleRate], a.[Channels], a.[Year], a.[FileSize], a.[CoverArtPath], a.[Fingerprint], a.[CreatedAt], a.[UpdatedAt],
                           a.[EnrichmentStatus]
                    FROM Audio a
                    JOIN AudioPlaylistItems api ON a.[GUID] = api.[AudioGUID]
                    WHERE api.[PlaylistGUID] = @pg
                    ORDER BY api.[OrderIndex]";
                cmd.Parameters.AddWithValue("@pg", playlistGuid);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    records.Add(new AudioRecord
                    {
                        GUID = reader.GetString(0),
                        FileName = reader.GetString(1),
                        Title = reader.GetString(2),
                        Extension = reader.GetString(3),
                        AlternativeTitle = reader.IsDBNull(4) ? null : reader.GetString(4),
                        Source = reader.GetString(5),
                        YoutubeID = reader.IsDBNull(6) ? null : reader.GetString(6),
                        FilePath = reader.GetString(7),
                        Artist = reader.IsDBNull(8) ? null : reader.GetString(8),
                        Album = reader.IsDBNull(9) ? null : reader.GetString(9),
                        Genre = reader.IsDBNull(10) ? null : reader.GetString(10),
                        Duration = reader.IsDBNull(11) ? null : reader.GetDouble(11),
                        Bitrate = reader.IsDBNull(12) ? null : reader.GetInt32(12),
                        SampleRate = reader.IsDBNull(13) ? null : reader.GetInt32(13),
                        Channels = reader.IsDBNull(14) ? null : reader.GetInt32(14),
                        Year = reader.IsDBNull(15) ? null : reader.GetInt32(15),
                        FileSize = reader.IsDBNull(16) ? null : reader.GetInt64(16),
                        CoverArtPath = reader.IsDBNull(17) ? null : reader.GetString(17),
                        Fingerprint = reader.IsDBNull(18) ? null : reader.GetString(18),
                        CreatedAt = reader.GetDateTime(19),
                        UpdatedAt = reader.GetDateTime(20),
                        EnrichmentStatus = reader.GetInt32(21)
                    });
                }
            }
            catch (Exception ex) { _logger.LogError(ex, "Error getting audio in playlist {Guid}", playlistGuid); }
            return records;
        }

        public async Task<HashSet<string>> GetAudioGuidsInAnyPlaylistAsync()
        {
            var guids = new HashSet<string>();
            try
            {
                using var connection = new SqliteConnection(_mediaDb.GetConnectionString());
                await connection.OpenAsync();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT DISTINCT [AudioGUID] FROM AudioPlaylistItems";
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    guids.Add(reader.GetString(0));
                }
            }
            catch (Exception ex) { _logger.LogError(ex, "Error getting all audio guids in playlists"); }
            return guids;
        }

        public async Task<string?> GenerateFingerprintAsync(string filePath)
        {
            try
            {
                var result = await _toolService.RunCommandAsync("fpcalc", $"\"{filePath}\" -json");
                if (result.ExitCode != 0) return null;

                using var doc = System.Text.Json.JsonDocument.Parse(result.StandardOutput);
                if (doc.RootElement.TryGetProperty("fingerprint", out var fp))
                {
                    return fp.GetString();
                }
                return null;
            }
            catch { return null; }
        }

        public async Task DeleteAudioAsync(string guid)
        {
            try
            {
                string? filePath = null;
                string? coverArtPath = null;

                // 1. Fetch file paths first
                using (var connection = new SqliteConnection(_mediaDb.GetConnectionString()))
                {
                    await connection.OpenAsync();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "SELECT FilePath, CoverArtPath FROM Audio WHERE GUID = @guid";
                    cmd.Parameters.AddWithValue("@guid", guid);
                    using var reader = await cmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        filePath = reader.IsDBNull(0) ? null : reader.GetString(0);
                        coverArtPath = reader.IsDBNull(1) ? null : reader.GetString(1);
                    }
                }

                // 2. Delete physical files from disk
                if (!string.IsNullOrEmpty(filePath) && System.IO.File.Exists(filePath))
                {
                    try
                    {
                        System.IO.File.Delete(filePath);
                        _logger.LogInformation("Deleted audio file: {Path}", filePath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("Failed to delete physical audio file: {Error}", ex.Message);
                    }
                }

                if (!string.IsNullOrEmpty(coverArtPath) && System.IO.File.Exists(coverArtPath))
                {
                    try
                    {
                        System.IO.File.Delete(coverArtPath);
                        _logger.LogInformation("Deleted cover art file: {Path}", coverArtPath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("Failed to delete physical cover art file: {Error}", ex.Message);
                    }
                }

                // Delete backup cover art if it exists
                var libraryPath = await _settingsService.GetSettingAsync("LibraryPath");
                if (!string.IsNullOrEmpty(libraryPath))
                {
                    var backupArtPath = Path.Combine(libraryPath, "Art", "Backup", $"{guid}.jpg");
                    if (System.IO.File.Exists(backupArtPath))
                    {
                        try
                        {
                            System.IO.File.Delete(backupArtPath);
                            _logger.LogInformation("Deleted backup cover art file: {Path}", backupArtPath);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning("Failed to delete physical backup cover art file: {Error}", ex.Message);
                        }
                    }
                }

                // 3. Delete records from database
                using (var connection = new SqliteConnection(_mediaDb.GetConnectionString()))
                {
                    await connection.OpenAsync();
                    using var transaction = connection.BeginTransaction();
                    try
                    {
                        // Delete from playlist items
                        var cmdPlaylist = connection.CreateCommand();
                        cmdPlaylist.Transaction = transaction;
                        cmdPlaylist.CommandText = "DELETE FROM AudioPlaylistItems WHERE AudioGUID = @guid";
                        cmdPlaylist.Parameters.AddWithValue("@guid", guid);
                        await cmdPlaylist.ExecuteNonQueryAsync();

                        // Delete from backups
                        var cmdBackup = connection.CreateCommand();
                        cmdBackup.Transaction = transaction;
                        cmdBackup.CommandText = "DELETE FROM AudioBackup WHERE GUID = @guid";
                        cmdBackup.Parameters.AddWithValue("@guid", guid);
                        await cmdBackup.ExecuteNonQueryAsync();

                        // Delete from Audio
                        var cmdAudio = connection.CreateCommand();
                        cmdAudio.Transaction = transaction;
                        cmdAudio.CommandText = "DELETE FROM Audio WHERE GUID = @guid";
                        cmdAudio.Parameters.AddWithValue("@guid", guid);
                        await cmdAudio.ExecuteNonQueryAsync();

                        transaction.Commit();
                        _logger.LogInformation("Deleted audio records from database for GUID: {GUID}", guid);
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        _logger.LogError(ex, "Transaction failed while deleting audio record {GUID}", guid);
                        throw;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting audio record {GUID}", guid);
                throw;
            }
        }

        public async Task<string> SaveCoverArtAsync(string guid, Stream imageStream)
        {
            var libraryPath = await _settingsService.GetSettingAsync("LibraryPath");
            if (string.IsNullOrEmpty(libraryPath))
            {
                throw new InvalidOperationException("Library path is not configured. Please set it in Settings.");
            }

            var artFolder = Path.Combine(libraryPath, "Art", "Audio");
            if (!Directory.Exists(artFolder))
            {
                Directory.CreateDirectory(artFolder);
            }

            var artPath = Path.Combine(artFolder, $"{guid}.jpg");

            // Read the stream into a MemoryStream to decode with SkiaSharp
            using var ms = new MemoryStream();
            await imageStream.CopyToAsync(ms);
            ms.Position = 0;

            using var original = SKBitmap.Decode(ms);
            if (original == null)
            {
                throw new InvalidOperationException("Failed to decode uploaded image.");
            }

            int size = Math.Min(original.Width, original.Height);
            int x = (original.Width - size) / 2;
            int y = (original.Height - size) / 2;

            using var subset = new SKBitmap(size, size);
            original.ExtractSubset(subset, new SKRectI(x, y, x + size, y + size));
            
            using var image = SKImage.FromBitmap(subset);
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
            
            // Delete old file if exists
            if (System.IO.File.Exists(artPath))
            {
                try
                {
                    System.IO.File.Delete(artPath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Failed to delete existing cover art file {Path}: {Error}", artPath, ex.Message);
                }
            }

            using (var stream = System.IO.File.OpenWrite(artPath))
            {
                data.SaveTo(stream);
            }

            _logger.LogInformation("Saved cover art to: {Path}", artPath);
            return artPath;
        }
    }
}

