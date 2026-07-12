using Autonomuse.Domain.Entities;
using Autonomuse.Infrastructure.Data;
using Autonomuse.Shared.Contracts;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using TagLib;

namespace Autonomuse.Services.Orchestration
{
    public class VideoService : IVideoService
    {
        private readonly MediaDatabaseService _mediaDb;
        private readonly ISettingsService _settingsService;
        private readonly ILogger<VideoService> _logger;

        private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".mkv", ".avi", ".mov", ".webm"
        };

        public VideoService(MediaDatabaseService mediaDb, ISettingsService settingsService, ILogger<VideoService> logger)
        {
            _mediaDb = mediaDb;
            _settingsService = settingsService;
            _logger = logger;
        }

        public async Task<VideoRecord> AddVideoAsync(string sourceFilePath, string source)
        {
            var fileName = Path.GetFileName(sourceFilePath);
            var extension = Path.GetExtension(sourceFilePath).ToLowerInvariant();
            var title = Path.GetFileNameWithoutExtension(sourceFilePath);

            if (!SupportedExtensions.Contains(extension))
                throw new InvalidOperationException($"Unsupported video format: {extension}");

            var libraryPath = await _settingsService.GetSettingAsync("LibraryPath")
                ?? throw new InvalidOperationException("Library path not configured.");

            var videoFolder = Path.Combine(libraryPath, "Video");
            if (!Directory.Exists(videoFolder)) Directory.CreateDirectory(videoFolder);

            var destinationPath = Path.Combine(videoFolder, fileName);
            if (System.IO.File.Exists(destinationPath))
            {
                var seg = Guid.NewGuid().ToString()[..8];
                destinationPath = Path.Combine(videoFolder, $"{title}_{seg}{extension}");
                fileName = Path.GetFileName(destinationPath);
                title = Path.GetFileNameWithoutExtension(destinationPath);
            }

            System.IO.File.Copy(sourceFilePath, destinationPath, false);
            var fileInfo = new FileInfo(destinationPath);

            var record = new VideoRecord
            {
                GUID = Guid.NewGuid().ToString(),
                FileName = fileName, Title = title, Extension = extension,
                AlternativeTitle = title,
                Source = source, FilePath = destinationPath,
                FileSize = fileInfo.Length,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
            };

            // Extract Metadata using TagLib
            try
            {
                using (var tfile = TagLib.File.Create(destinationPath))
                {
                    if (tfile.Properties != null)
                    {
                        record.Duration = tfile.Properties.Duration.TotalSeconds;
                        record.Resolution = $"{tfile.Properties.VideoWidth}x{tfile.Properties.VideoHeight}";
                        record.Bitrate = tfile.Properties.AudioBitrate; // VideoBitrate not always available in base Properties
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
                        if (tfile.Tag.Year > 0) record.Year = (int)tfile.Tag.Year;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to extract metadata for {File}: {Error}", destinationPath, ex.Message);
            }

            await AddVideoRecordAsync(record);

            _logger.LogInformation("Added video: {Title}", record.Title);
            return record;
        }

        public async Task AddVideoRecordAsync(VideoRecord record)
        {
            try
            {
                using var conn = new SqliteConnection(_mediaDb.GetConnectionString());
                await conn.OpenAsync();
                var cmd = conn.CreateCommand();
                cmd.CommandText = @"INSERT INTO Video ([GUID],[FileName],[Title],[Extension],[AlternativeTitle],[Source],[YoutubeID],[FilePath],[Genre],[Artist],[Duration],[Resolution],[Bitrate],[SampleRate],[Channels],[Year],[FileSize],[ThumbnailPath],[MetadataStatus],[CreatedAt],[UpdatedAt])
                    VALUES (@g,@fn,@t,@ext,@at,@src,@yid,@fp,@genre,@artist,@dur,@res,@br,@sr,@ch,@year,@fs,@th,@ms,@ca,@ua)";
                cmd.Parameters.AddWithValue("@g", record.GUID);
                cmd.Parameters.AddWithValue("@fn", record.FileName);
                cmd.Parameters.AddWithValue("@t", record.Title);
                cmd.Parameters.AddWithValue("@ext", record.Extension);
                cmd.Parameters.AddWithValue("@at", (object?)record.AlternativeTitle ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@src", record.Source);
                cmd.Parameters.AddWithValue("@yid", (object?)record.YoutubeID ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@fp", record.FilePath);
                cmd.Parameters.AddWithValue("@genre", (object?)record.Genre ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@artist", (object?)record.Artist ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@dur", (object?)record.Duration ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@res", (object?)record.Resolution ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@br", (object?)record.Bitrate ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@sr", (object?)record.SampleRate ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ch", (object?)record.Channels ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@year", (object?)record.Year ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@fs", (object?)record.FileSize ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@th", (object?)record.ThumbnailPath ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ms", record.MetadataStatus);
                cmd.Parameters.AddWithValue("@ca", record.CreatedAt);
                cmd.Parameters.AddWithValue("@ua", record.UpdatedAt);
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error inserting video record {Title}", record.Title);
                throw;
            }
        }

        public async Task<List<VideoRecord>> GetAllVideosAsync()
        {
            var records = new List<VideoRecord>();
            using var conn = new SqliteConnection(_mediaDb.GetConnectionString());
            await conn.OpenAsync();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT [GUID],[FileName],[Title],[Extension],[AlternativeTitle],[Source],[YoutubeID],[FilePath],[Genre],[Artist],[Duration],[Resolution],[Bitrate],[SampleRate],[Channels],[Year],[FileSize],[ThumbnailPath],[MetadataStatus],[CreatedAt],[UpdatedAt] FROM Video ORDER BY [CreatedAt] DESC";
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                records.Add(new VideoRecord
                {
                    GUID = r.GetString(0),
                    FileName = r.GetString(1),
                    Title = r.GetString(2),
                    Extension = r.GetString(3),
                    AlternativeTitle = r.IsDBNull(4) ? null : r.GetString(4),
                    Source = r.GetString(5),
                    YoutubeID = r.IsDBNull(6) ? null : r.GetString(6),
                    FilePath = r.GetString(7),
                    Genre = r.IsDBNull(8) ? null : r.GetString(8),
                    Artist = r.IsDBNull(9) ? null : r.GetString(9),
                    Duration = r.IsDBNull(10) ? null : r.GetDouble(10),
                    Resolution = r.IsDBNull(11) ? null : r.GetString(11),
                    Bitrate = r.IsDBNull(12) ? null : r.GetInt32(12),
                    SampleRate = r.IsDBNull(13) ? null : r.GetInt32(13),
                    Channels = r.IsDBNull(14) ? null : r.GetInt32(14),
                    Year = r.IsDBNull(15) ? null : r.GetInt32(15),
                    FileSize = r.IsDBNull(16) ? null : r.GetInt64(16),
                    ThumbnailPath = r.IsDBNull(17) ? null : r.GetString(17),
                    MetadataStatus = r.GetInt32(18),
                    CreatedAt = r.GetDateTime(19),
                    UpdatedAt = r.GetDateTime(20)
                });
            }
            return records;
        }

        public async Task<List<VideoPlaylist>> GetPlaylistsAsync()
        {
            var list = new List<VideoPlaylist>();
            using var conn = new SqliteConnection(_mediaDb.GetConnectionString());
            await conn.OpenAsync();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT [GUID],[Name],[Description],[CreatedAt],[UpdatedAt] FROM VideoPlaylist ORDER BY [Name]";
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                list.Add(new VideoPlaylist
                {
                    GUID = r.GetString(0), Name = r.GetString(1),
                    Description = r.IsDBNull(2) ? null : r.GetString(2),
                    CreatedAt = r.GetDateTime(3), UpdatedAt = r.GetDateTime(4)
                });
            }
            return list;
        }

        public async Task<VideoPlaylist> CreatePlaylistAsync(string name, string? description = null)
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

            var pl = new VideoPlaylist { GUID = Guid.NewGuid().ToString(), Name = name, Description = description };
            using var conn = new SqliteConnection(_mediaDb.GetConnectionString());
            await conn.OpenAsync();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO VideoPlaylist ([GUID],[Name],[Description],[CreatedAt],[UpdatedAt]) VALUES (@g,@n,@d,@c,@u)";
            cmd.Parameters.AddWithValue("@g", pl.GUID);
            cmd.Parameters.AddWithValue("@n", pl.Name);
            cmd.Parameters.AddWithValue("@d", (object?)pl.Description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@c", pl.CreatedAt);
            cmd.Parameters.AddWithValue("@u", pl.UpdatedAt);
            await cmd.ExecuteNonQueryAsync();
            return pl;
        }

        public async Task UpdatePlaylistDescriptionAsync(string guid, string description)
        {
            using var connection = new SqliteConnection(_mediaDb.GetConnectionString());
            await connection.OpenAsync();
            var cmd = connection.CreateCommand();
            cmd.CommandText = "UPDATE VideoPlaylist SET [Description] = @d, [UpdatedAt] = @u WHERE [GUID] = @g";
            cmd.Parameters.AddWithValue("@d", description);
            cmd.Parameters.AddWithValue("@u", DateTime.UtcNow);
            cmd.Parameters.AddWithValue("@g", guid);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task AddToPlaylistAsync(string playlistGuid, string videoGuid)
        {
            using var conn = new SqliteConnection(_mediaDb.GetConnectionString());
            await conn.OpenAsync();
            var countCmd = conn.CreateCommand();
            countCmd.CommandText = "SELECT COALESCE(MAX([OrderIndex]),-1)+1 FROM VideoPlaylistItems WHERE [PlaylistGUID]=@pg";
            countCmd.Parameters.AddWithValue("@pg", playlistGuid);
            var idx = Convert.ToInt32(await countCmd.ExecuteScalarAsync());

            var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO VideoPlaylistItems ([GUID],[PlaylistGUID],[VideoGUID],[OrderIndex],[AddedAt]) VALUES (@g,@pg,@vg,@oi,@a)";
            cmd.Parameters.AddWithValue("@g", Guid.NewGuid().ToString());
            cmd.Parameters.AddWithValue("@pg", playlistGuid);
            cmd.Parameters.AddWithValue("@vg", videoGuid);
            cmd.Parameters.AddWithValue("@oi", idx);
            cmd.Parameters.AddWithValue("@a", DateTime.UtcNow);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<VideoRecord?> GetVideoByTitleAndSourceAsync(string title, string source)
        {
            try
            {
                using var conn = new SqliteConnection(_mediaDb.GetConnectionString());
                await conn.OpenAsync();
                var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT [GUID],[FileName],[Title],[Extension],[AlternativeTitle],[Source],[YoutubeID],[FilePath],[Genre],[Artist],[Duration],[Resolution],[Bitrate],[SampleRate],[Channels],[Year],[FileSize],[ThumbnailPath],[MetadataStatus],[CreatedAt],[UpdatedAt] FROM Video WHERE [Title] = @title AND [Source] = @source COLLATE NOCASE LIMIT 1";
                cmd.Parameters.AddWithValue("@title", title);
                cmd.Parameters.AddWithValue("@source", source);

                using var r = await cmd.ExecuteReaderAsync();
                if (await r.ReadAsync())
                {
                    return new VideoRecord
                    {
                        GUID = r.GetString(0),
                        FileName = r.GetString(1),
                        Title = r.GetString(2),
                        Extension = r.GetString(3),
                        AlternativeTitle = r.IsDBNull(4) ? null : r.GetString(4),
                        Source = r.GetString(5),
                        YoutubeID = r.IsDBNull(6) ? null : r.GetString(6),
                        FilePath = r.GetString(7),
                        Genre = r.IsDBNull(8) ? null : r.GetString(8),
                        Artist = r.IsDBNull(9) ? null : r.GetString(9),
                        Duration = r.IsDBNull(10) ? null : r.GetDouble(10),
                        Resolution = r.IsDBNull(11) ? null : r.GetString(11),
                        Bitrate = r.IsDBNull(12) ? null : r.GetInt32(12),
                        SampleRate = r.IsDBNull(13) ? null : r.GetInt32(13),
                        Channels = r.IsDBNull(14) ? null : r.GetInt32(14),
                        Year = r.IsDBNull(15) ? null : r.GetInt32(15),
                        FileSize = r.IsDBNull(16) ? null : r.GetInt64(16),
                        ThumbnailPath = r.IsDBNull(17) ? null : r.GetString(17),
                        MetadataStatus = r.GetInt32(18),
                        CreatedAt = r.GetDateTime(19),
                        UpdatedAt = r.GetDateTime(20)
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting video by title {Title}", title);
            }
            return null;
        }

        public async Task<VideoRecord?> GetVideoByYoutubeIDAsync(string youtubeId)
        {
            try
            {
                using var conn = new SqliteConnection(_mediaDb.GetConnectionString());
                await conn.OpenAsync();
                var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT [GUID],[FileName],[Title],[Extension],[AlternativeTitle],[Source],[YoutubeID],[FilePath],[Genre],[Artist],[Duration],[Resolution],[Bitrate],[SampleRate],[Channels],[Year],[FileSize],[ThumbnailPath],[MetadataStatus],[CreatedAt],[UpdatedAt] FROM Video WHERE [YoutubeID] = @yid LIMIT 1";
                cmd.Parameters.AddWithValue("@yid", youtubeId);

                using var r = await cmd.ExecuteReaderAsync();
                if (await r.ReadAsync())
                {
                    return new VideoRecord
                    {
                        GUID = r.GetString(0),
                        FileName = r.GetString(1),
                        Title = r.GetString(2),
                        Extension = r.GetString(3),
                        AlternativeTitle = r.IsDBNull(4) ? null : r.GetString(4),
                        Source = r.GetString(5),
                        YoutubeID = r.IsDBNull(6) ? null : r.GetString(6),
                        FilePath = r.GetString(7),
                        Genre = r.IsDBNull(8) ? null : r.GetString(8),
                        Artist = r.IsDBNull(9) ? null : r.GetString(9),
                        Duration = r.IsDBNull(10) ? null : r.GetDouble(10),
                        Resolution = r.IsDBNull(11) ? null : r.GetString(11),
                        Bitrate = r.IsDBNull(12) ? null : r.GetInt32(12),
                        SampleRate = r.IsDBNull(13) ? null : r.GetInt32(13),
                        Channels = r.IsDBNull(14) ? null : r.GetInt32(14),
                        Year = r.IsDBNull(15) ? null : r.GetInt32(15),
                        FileSize = r.IsDBNull(16) ? null : r.GetInt64(16),
                        ThumbnailPath = r.IsDBNull(17) ? null : r.GetString(17),
                        MetadataStatus = r.GetInt32(18),
                        CreatedAt = r.GetDateTime(19),
                        UpdatedAt = r.GetDateTime(20)
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting video by YoutubeID {Id}", youtubeId);
            }
            return null;
        }

        public async Task<List<string>> GetPlaylistsForVideoAsync(string videoGuid)
        {
            var list = new List<string>();
            try
            {
                using var conn = new SqliteConnection(_mediaDb.GetConnectionString());
                await conn.OpenAsync();
                var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT [PlaylistGUID] FROM VideoPlaylistItems WHERE [VideoGUID] = @vg";
                cmd.Parameters.AddWithValue("@vg", videoGuid);

                using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    list.Add(r.GetString(0));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting playlists for video {Guid}", videoGuid);
            }
            return list;
        }

        public async Task UpdateVideoRecordAsync(VideoRecord record)
        {
            try
            {
                using var connection = new SqliteConnection(_mediaDb.GetConnectionString());
                await connection.OpenAsync();

                var command = connection.CreateCommand();
                command.CommandText = @"
                    UPDATE Video SET 
                        [Title] = @title, [AlternativeTitle] = @altTitle, [Artist] = @artist, 
                        [Genre] = @genre, [Year] = @year, 
                        [ThumbnailPath] = @thumbnail, [MetadataStatus] = @ms, [UpdatedAt] = @updatedAt
                    WHERE [GUID] = @guid;
                ";

                command.Parameters.AddWithValue("@title", record.Title);
                command.Parameters.AddWithValue("@altTitle", (object?)record.AlternativeTitle ?? DBNull.Value);
                command.Parameters.AddWithValue("@artist", (object?)record.Artist ?? DBNull.Value);
                command.Parameters.AddWithValue("@genre", (object?)record.Genre ?? DBNull.Value);
                command.Parameters.AddWithValue("@year", (object?)record.Year ?? DBNull.Value);
                command.Parameters.AddWithValue("@thumbnail", (object?)record.ThumbnailPath ?? DBNull.Value);
                command.Parameters.AddWithValue("@ms", record.MetadataStatus);
                command.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow);
                command.Parameters.AddWithValue("@guid", record.GUID);

                await command.ExecuteNonQueryAsync();
                _logger.LogInformation("Updated video record: {Title} ({GUID})", record.Title, record.GUID);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating video record {Title}", record.Title);
                throw;
            }
        }

        public async Task UpdatePhysicalTagsAsync(VideoRecord record)
        {
            try
            {
                if (!System.IO.File.Exists(record.FilePath))
                {
                    _logger.LogWarning("Cannot update tags: file not found at {Path}", record.FilePath);
                    return;
                }

                await Task.Run(() =>
                {
                    using var tfile = TagLib.File.Create(record.FilePath);
                    tfile.Tag.Title = record.AlternativeTitle ?? record.Title;
                    if (!string.IsNullOrEmpty(record.Artist))
                    {
                        tfile.Tag.Performers = new[] { record.Artist };
                    }
                    if (record.Year.HasValue && record.Year > 0)
                    {
                        tfile.Tag.Year = (uint)record.Year.Value;
                    }
                    if (!string.IsNullOrEmpty(record.Genre))
                    {
                        tfile.Tag.Genres = new[] { record.Genre };
                    }
                    tfile.Save();
                });

                _logger.LogInformation("Successfully updated physical tags for video {Title}", record.Title);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating physical tags for video {Title}", record.Title);
            }
        }

        public async Task DeleteVideoAsync(string guid)
        {
            try
            {
                string? filePath = null;
                string? thumbnailPath = null;

                using (var connection = new SqliteConnection(_mediaDb.GetConnectionString()))
                {
                    await connection.OpenAsync();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "SELECT FilePath, ThumbnailPath FROM Video WHERE GUID = @guid";
                    cmd.Parameters.AddWithValue("@guid", guid);
                    using var reader = await cmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        filePath = reader.IsDBNull(0) ? null : reader.GetString(0);
                        thumbnailPath = reader.IsDBNull(1) ? null : reader.GetString(1);
                    }
                }

                if (!string.IsNullOrEmpty(filePath) && System.IO.File.Exists(filePath))
                {
                    try
                    {
                        System.IO.File.Delete(filePath);
                        _logger.LogInformation("Deleted video file: {Path}", filePath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("Failed to delete physical video file: {Error}", ex.Message);
                    }
                }

                if (!string.IsNullOrEmpty(thumbnailPath) && System.IO.File.Exists(thumbnailPath))
                {
                    try
                    {
                        System.IO.File.Delete(thumbnailPath);
                        _logger.LogInformation("Deleted thumbnail file: {Path}", thumbnailPath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("Failed to delete physical thumbnail file: {Error}", ex.Message);
                    }
                }

                // Delete backup thumbnail if it exists
                var libraryPath = await _settingsService.GetSettingAsync("LibraryPath");
                if (!string.IsNullOrEmpty(libraryPath))
                {
                    var backupArtPath = Path.Combine(libraryPath, "Art", "Video", "Backup", $"{guid}.jpg");
                    if (System.IO.File.Exists(backupArtPath))
                    {
                        try
                        {
                            System.IO.File.Delete(backupArtPath);
                            _logger.LogInformation("Deleted backup thumbnail file: {Path}", backupArtPath);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning("Failed to delete physical backup thumbnail file: {Error}", ex.Message);
                        }
                    }

                    // Clean up VideoBackup thumbnail if it exists (from metadata backup system)
                    var videoBackupArtPath = Path.Combine(libraryPath, "Art", "VideoBackup", $"{guid}.jpg");
                    if (System.IO.File.Exists(videoBackupArtPath))
                    {
                        try
                        {
                            System.IO.File.Delete(videoBackupArtPath);
                            _logger.LogInformation("Deleted video backup thumbnail file: {Path}", videoBackupArtPath);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning("Failed to delete video backup thumbnail file: {Error}", ex.Message);
                        }
                    }
                }

                using (var connection = new SqliteConnection(_mediaDb.GetConnectionString()))
                {
                    await connection.OpenAsync();
                    using var transaction = connection.BeginTransaction();
                    try
                    {
                        var cmdPlaylist = connection.CreateCommand();
                        cmdPlaylist.Transaction = transaction;
                        cmdPlaylist.CommandText = "DELETE FROM VideoPlaylistItems WHERE VideoGUID = @guid";
                        cmdPlaylist.Parameters.AddWithValue("@guid", guid);
                        await cmdPlaylist.ExecuteNonQueryAsync();

                        var cmdBackup = connection.CreateCommand();
                        cmdBackup.Transaction = transaction;
                        cmdBackup.CommandText = "DELETE FROM VideoBackup WHERE GUID = @guid";
                        cmdBackup.Parameters.AddWithValue("@guid", guid);
                        await cmdBackup.ExecuteNonQueryAsync();

                        var cmdVideo = connection.CreateCommand();
                        cmdVideo.Transaction = transaction;
                        cmdVideo.CommandText = "DELETE FROM Video WHERE GUID = @guid";
                        cmdVideo.Parameters.AddWithValue("@guid", guid);
                        await cmdVideo.ExecuteNonQueryAsync();

                        transaction.Commit();
                        _logger.LogInformation("Deleted video records from database for GUID: {GUID}", guid);
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        _logger.LogError(ex, "Transaction failed while deleting video record {GUID}", guid);
                        throw;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting video record {GUID}", guid);
                throw;
            }
        }

        public async Task<string> SaveThumbnailAsync(string guid, Stream imageStream)
        {
            var libraryPath = await _settingsService.GetSettingAsync("LibraryPath");
            if (string.IsNullOrEmpty(libraryPath))
            {
                throw new InvalidOperationException("Library path is not configured. Please set it in Settings.");
            }

            var artFolder = Path.Combine(libraryPath, "Art", "Video");
            if (!Directory.Exists(artFolder))
            {
                Directory.CreateDirectory(artFolder);
            }

            var artPath = Path.Combine(artFolder, $"{guid}.jpg");
            using (var fileStream = new FileStream(artPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await imageStream.CopyToAsync(fileStream);
            }

            // Create backup of thumbnail
            var backupFolder = Path.Combine(artFolder, "Backup");
            if (!Directory.Exists(backupFolder))
            {
                Directory.CreateDirectory(backupFolder);
            }
            var backupPath = Path.Combine(backupFolder, $"{guid}.jpg");
            try
            {
                System.IO.File.Copy(artPath, backupPath, true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to create backup copy of thumbnail: {Error}", ex.Message);
            }

            return artPath;
        }

        public async Task<List<VideoRecord>> GetVideoInPlaylistAsync(string playlistGuid)
        {
            var list = new List<VideoRecord>();
            try
            {
                using var conn = new SqliteConnection(_mediaDb.GetConnectionString());
                await conn.OpenAsync();
                var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT v.[GUID], v.[FileName], v.[Title], v.[Extension], v.[AlternativeTitle], 
                           v.[Source], v.[YoutubeID], v.[FilePath], v.[Genre], v.[Artist], 
                           v.[Duration], v.[Resolution], v.[Bitrate], v.[SampleRate], v.[Channels], 
                           v.[Year], v.[FileSize], v.[ThumbnailPath], v.[MetadataStatus], v.[CreatedAt], v.[UpdatedAt] 
                    FROM Video v
                    INNER JOIN VideoPlaylistItems pi ON v.[GUID] = pi.[VideoGUID]
                    WHERE pi.[PlaylistGUID] = @pg
                    ORDER BY pi.[OrderIndex]
                ";
                cmd.Parameters.AddWithValue("@pg", playlistGuid);

                using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    list.Add(new VideoRecord
                    {
                        GUID = r.GetString(0),
                        FileName = r.GetString(1),
                        Title = r.GetString(2),
                        Extension = r.GetString(3),
                        AlternativeTitle = r.IsDBNull(4) ? null : r.GetString(4),
                        Source = r.GetString(5),
                        YoutubeID = r.IsDBNull(6) ? null : r.GetString(6),
                        FilePath = r.GetString(7),
                        Genre = r.IsDBNull(8) ? null : r.GetString(8),
                        Artist = r.IsDBNull(9) ? null : r.GetString(9),
                        Duration = r.IsDBNull(10) ? null : r.GetDouble(10),
                        Resolution = r.IsDBNull(11) ? null : r.GetString(11),
                        Bitrate = r.IsDBNull(12) ? null : r.GetInt32(12),
                        SampleRate = r.IsDBNull(13) ? null : r.GetInt32(13),
                        Channels = r.IsDBNull(14) ? null : r.GetInt32(14),
                        Year = r.IsDBNull(15) ? null : r.GetInt32(15),
                        FileSize = r.IsDBNull(16) ? null : r.GetInt64(16),
                        ThumbnailPath = r.IsDBNull(17) ? null : r.GetString(17),
                        CreatedAt = r.GetDateTime(18),
                        UpdatedAt = r.GetDateTime(19)
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting video in playlist {Guid}", playlistGuid);
            }
            return list;
        }

        public async Task<HashSet<string>> GetVideoGuidsInAnyPlaylistAsync()
        {
            var guids = new HashSet<string>();
            try
            {
                using var conn = new SqliteConnection(_mediaDb.GetConnectionString());
                await conn.OpenAsync();
                var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT DISTINCT [VideoGUID] FROM VideoPlaylistItems";
                using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    guids.Add(r.GetString(0));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting video guids in playlists");
            }
            return guids;
        }

        public async Task CreateVideoBackupAsync(VideoBackup backup)
        {
            try
            {
                using var connection = new SqliteConnection(_mediaDb.GetConnectionString());
                await connection.OpenAsync();

                var command = connection.CreateCommand();
                command.CommandText = @"
                    INSERT OR IGNORE INTO VideoBackup (
                        GUID, AlternativeTitle, Artist, Genre, Year, ThumbnailPath, CreatedAt
                    ) VALUES (
                        @guid, @altTitle, @artist, @genre, @year, @thumbnail, @createdAt
                    );";

                command.Parameters.AddWithValue("@guid", backup.GUID);
                command.Parameters.AddWithValue("@altTitle", (object?)backup.AlternativeTitle ?? DBNull.Value);
                command.Parameters.AddWithValue("@artist", (object?)backup.Artist ?? DBNull.Value);
                command.Parameters.AddWithValue("@genre", (object?)backup.Genre ?? DBNull.Value);
                command.Parameters.AddWithValue("@year", (object?)backup.Year ?? DBNull.Value);
                command.Parameters.AddWithValue("@thumbnail", (object?)backup.ThumbnailPath ?? DBNull.Value);
                command.Parameters.AddWithValue("@createdAt", backup.CreatedAt);

                await command.ExecuteNonQueryAsync();
                _logger.LogInformation("Created metadata backup for video: {GUID}", backup.GUID);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating video backup for {GUID}", backup.GUID);
                throw;
            }
        }

        public async Task<VideoBackup?> GetVideoBackupAsync(string guid)
        {
            try
            {
                using var connection = new SqliteConnection(_mediaDb.GetConnectionString());
                await connection.OpenAsync();

                var command = connection.CreateCommand();
                command.CommandText = "SELECT * FROM VideoBackup WHERE GUID = @guid";
                command.Parameters.AddWithValue("@guid", guid);

                using var reader = await command.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return new VideoBackup
                    {
                        GUID = reader.GetString(0),
                        AlternativeTitle = reader.IsDBNull(1) ? null : reader.GetString(1),
                        Artist = reader.IsDBNull(2) ? null : reader.GetString(2),
                        Genre = reader.IsDBNull(3) ? null : reader.GetString(3),
                        Year = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                        ThumbnailPath = reader.IsDBNull(5) ? null : reader.GetString(5),
                        CreatedAt = reader.GetDateTime(6)
                    };
                }
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting video backup for {GUID}", guid);
                return null;
            }
        }

        public async Task DeleteVideoBackupAsync(string guid)
        {
            try
            {
                using var connection = new SqliteConnection(_mediaDb.GetConnectionString());
                await connection.OpenAsync();

                var command = connection.CreateCommand();
                command.CommandText = "DELETE FROM VideoBackup WHERE GUID = @guid";
                command.Parameters.AddWithValue("@guid", guid);

                await command.ExecuteNonQueryAsync();
                _logger.LogInformation("Deleted video backup for {GUID}", guid);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting video backup for {GUID}", guid);
            }
        }
    }
}
