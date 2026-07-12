using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Autonomuse.Infrastructure.Data
{
    public class MediaDatabaseService
    {
        private readonly string _dbPath;
        private readonly ILogger<MediaDatabaseService> _logger;

        public MediaDatabaseService(ILogger<MediaDatabaseService> logger)
        {
            _logger = logger;

            // Same folder as setting.db
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var dbFolder = Path.Combine(localAppData, "Autonomuse", "com.reversedhorizonstudio.autonomuse", "Data");

            if (!Directory.Exists(dbFolder))
            {
                Directory.CreateDirectory(dbFolder);
            }

            _dbPath = Path.Combine(dbFolder, "media.db");
            InitializeDatabase();
        }

        public string GetConnectionString() => $"Data Source={_dbPath}";

        private void InitializeDatabase()
        {
            try
            {
                using var connection = new SqliteConnection(GetConnectionString());
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = @"
                    -- ===== AUDIO =====
                    CREATE TABLE IF NOT EXISTS Audio (
                        [GUID] TEXT PRIMARY KEY,
                        [FileName] TEXT NOT NULL,
                        [Title] TEXT NOT NULL,
                        [Extension] TEXT NOT NULL,
                        [AlternativeTitle] TEXT,
                        [Source] TEXT NOT NULL,
                        [YoutubeID] TEXT,
                        [FilePath] TEXT NOT NULL,
                        [Artist] TEXT,
                        [Album] TEXT,
                        [Genre] TEXT,
                        [Duration] REAL,
                        [Bitrate] INTEGER,
                        [SampleRate] INTEGER,
                        [Channels] INTEGER,
                        [Year] INTEGER,
                        [FileSize] INTEGER,
                        [CoverArtPath] TEXT,
                        [Fingerprint] TEXT,
                        [CreatedAt] DATETIME NOT NULL,
                        [UpdatedAt] DATETIME NOT NULL,
                        [EnrichmentStatus] INTEGER NOT NULL DEFAULT 0
                    );

                    CREATE TABLE IF NOT EXISTS AudioPlaylist (
                        [GUID] TEXT PRIMARY KEY,
                        [Name] TEXT NOT NULL,
                        [Description] TEXT,
                        [CreatedAt] DATETIME NOT NULL,
                        [UpdatedAt] DATETIME NOT NULL
                    );

                    CREATE TABLE IF NOT EXISTS AudioPlaylistItems (
                        [GUID] TEXT PRIMARY KEY,
                        [PlaylistGUID] TEXT NOT NULL,
                        [AudioGUID] TEXT NOT NULL,
                        [OrderIndex] INTEGER NOT NULL,
                        [AddedAt] DATETIME NOT NULL,
                        FOREIGN KEY ([PlaylistGUID]) REFERENCES AudioPlaylist([GUID]),
                        FOREIGN KEY ([AudioGUID]) REFERENCES Audio([GUID])
                    );

                    CREATE TABLE IF NOT EXISTS AudioBackup (
                        [GUID] TEXT PRIMARY KEY,
                        [AlternativeTitle] TEXT,
                        [YoutubeID] TEXT,
                        [Artist] TEXT,
                        [Album] TEXT,
                        [Genre] TEXT,
                        [Year] INTEGER,
                        [CoverArtPath] TEXT,
                        [CreatedAt] DATETIME NOT NULL,
                        FOREIGN KEY ([GUID]) REFERENCES Audio([GUID])
                    );

                    -- ===== VIDEO =====
                    CREATE TABLE IF NOT EXISTS Video (
                        [GUID] TEXT PRIMARY KEY,
                        [FileName] TEXT NOT NULL,
                        [Title] TEXT NOT NULL,
                        [Extension] TEXT NOT NULL,
                        [AlternativeTitle] TEXT,
                        [Source] TEXT NOT NULL,
                        [YoutubeID] TEXT,
                        [FilePath] TEXT NOT NULL,
                        [Genre] TEXT,
                        [Artist] TEXT,
                        [Duration] REAL,
                        [Resolution] TEXT,
                        [Bitrate] INTEGER,
                        [SampleRate] INTEGER,
                        [Channels] INTEGER,
                        [Year] INTEGER,
                        [FileSize] INTEGER,
                        [ThumbnailPath] TEXT,
                        [MetadataStatus] INTEGER NOT NULL DEFAULT 0,
                        [CreatedAt] DATETIME NOT NULL,
                        [UpdatedAt] DATETIME NOT NULL
                    );

                    CREATE TABLE IF NOT EXISTS VideoPlaylist (
                        [GUID] TEXT PRIMARY KEY,
                        [Name] TEXT NOT NULL,
                        [Description] TEXT,
                        [CreatedAt] DATETIME NOT NULL,
                        [UpdatedAt] DATETIME NOT NULL
                    );

                    CREATE TABLE IF NOT EXISTS VideoPlaylistItems (
                        [GUID] TEXT PRIMARY KEY,
                        [PlaylistGUID] TEXT NOT NULL,
                        [VideoGUID] TEXT NOT NULL,
                        [OrderIndex] INTEGER NOT NULL,
                        [AddedAt] DATETIME NOT NULL,
                        FOREIGN KEY ([PlaylistGUID]) REFERENCES VideoPlaylist([GUID]),
                        FOREIGN KEY ([VideoGUID]) REFERENCES Video([GUID])
                    );

                    CREATE TABLE IF NOT EXISTS VideoBackup (
                        [GUID] TEXT PRIMARY KEY,
                        [AlternativeTitle] TEXT,
                        [Artist] TEXT,
                        [Genre] TEXT,
                        [Year] INTEGER,
                        [ThumbnailPath] TEXT,
                        [CreatedAt] DATETIME NOT NULL,
                        FOREIGN KEY ([GUID]) REFERENCES Video([GUID])
                    );

                    -- ===== IMAGES =====
                    CREATE TABLE IF NOT EXISTS Image (
                        [GUID] TEXT PRIMARY KEY,
                        [FileName] TEXT NOT NULL,
                        [Title] TEXT NOT NULL,
                        [Extension] TEXT NOT NULL,
                        [Source] TEXT NOT NULL,
                        [FilePath] TEXT NOT NULL,
                        [Width] INTEGER,
                        [Height] INTEGER,
                        [FileSize] INTEGER,
                        [PerceptualHash] INTEGER,
                        [CreatedAt] DATETIME NOT NULL,
                        [UpdatedAt] DATETIME NOT NULL
                    );

                    CREATE TABLE IF NOT EXISTS ImageAlbum (
                        [GUID] TEXT PRIMARY KEY,
                        [Name] TEXT NOT NULL,
                        [Description] TEXT,
                        [CoverImageGUID] TEXT,
                        [CreatedAt] DATETIME NOT NULL,
                        [UpdatedAt] DATETIME NOT NULL
                    );

                    CREATE TABLE IF NOT EXISTS ImageAlbumItems (
                        [GUID] TEXT PRIMARY KEY,
                        [AlbumGUID] TEXT NOT NULL,
                        [ImageGUID] TEXT NOT NULL,
                        [OrderIndex] INTEGER NOT NULL,
                        [AddedAt] DATETIME NOT NULL,
                        FOREIGN KEY ([AlbumGUID]) REFERENCES ImageAlbum([GUID]),
                        FOREIGN KEY ([ImageGUID]) REFERENCES Image([GUID])
                    );

                    -- ===== E-BOOKS =====
                    CREATE TABLE IF NOT EXISTS EBook (
                        [GUID] TEXT PRIMARY KEY,
                        [FileName] TEXT NOT NULL,
                        [Title] TEXT NOT NULL,
                        [Extension] TEXT NOT NULL,
                        [Source] TEXT NOT NULL,
                        [FilePath] TEXT NOT NULL,
                        [Author] TEXT,
                        [PageCount] INTEGER,
                        [FileSize] INTEGER,
                        [CoverPath] TEXT,
                        [CreatedAt] DATETIME NOT NULL,
                        [UpdatedAt] DATETIME NOT NULL
                    );

                    CREATE TABLE IF NOT EXISTS EBookSeries (
                        [GUID] TEXT PRIMARY KEY,
                        [Name] TEXT NOT NULL,
                        [Description] TEXT,
                        [CreatedAt] DATETIME NOT NULL,
                        [UpdatedAt] DATETIME NOT NULL
                    );

                    CREATE TABLE IF NOT EXISTS EBookChapter (
                        [GUID] TEXT PRIMARY KEY,
                        [SeriesGUID] TEXT NOT NULL,
                        [ChapterNumber] INTEGER NOT NULL,
                        [Title] TEXT NOT NULL,
                        [CreatedAt] DATETIME NOT NULL,
                        [UpdatedAt] DATETIME NOT NULL,
                        FOREIGN KEY ([SeriesGUID]) REFERENCES EBookSeries([GUID])
                    );

                    CREATE TABLE IF NOT EXISTS EBookChapterPages (
                        [GUID] TEXT PRIMARY KEY,
                        [ChapterGUID] TEXT NOT NULL,
                        [EBookGUID] TEXT NOT NULL,
                        [PageIndex] INTEGER NOT NULL,
                        [AddedAt] DATETIME NOT NULL,
                        FOREIGN KEY ([ChapterGUID]) REFERENCES EBookChapter([GUID]),
                        FOREIGN KEY ([EBookGUID]) REFERENCES EBook([GUID])
                    );
                ";
                command.ExecuteNonQuery();
                var verifyCmd = connection.CreateCommand();
                verifyCmd.CommandText = @"
                    SELECT count(*) FROM sqlite_master 
                    WHERE type='table' AND name IN (
                        'Audio', 'AudioPlaylist', 'AudioPlaylistItems',
                        'Video', 'VideoPlaylist', 'VideoPlaylistItems',
                        'Image', 'ImageAlbum', 'ImageAlbumItems',
                        'EBook', 'EBookSeries', 'EBookChapter', 'EBookChapterPages'
                    );
                ";
                var tableCount = Convert.ToInt32(verifyCmd.ExecuteScalar());

                if (tableCount == 14)
                {
                    _logger.LogInformation("Media database verified successfully at {Path}. All 14 tables present.", _dbPath);
                }
                else
                {
                    _logger.LogError("Media database initialization incomplete. Expected 14 tables, found {Count}", tableCount);
                }

                // Migrations for existing databases
                try
                {
                    var migrateCmd = connection.CreateCommand();
                    migrateCmd.CommandText = "ALTER TABLE Video ADD COLUMN [MetadataStatus] INTEGER NOT NULL DEFAULT 0";
                    migrateCmd.ExecuteNonQuery();
                    _logger.LogInformation("Added MetadataStatus column to Video table.");
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("Migration for MetadataStatus column skipped (likely already exists): {Error}", ex.Message);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize media database");
            }
        }
        public async Task ClearAllDataAsync()
        {
            try
            {
                using var connection = new SqliteConnection(GetConnectionString());
                await connection.OpenAsync();

                string[] tables = { 
                    "AudioPlaylistItems", "AudioPlaylist", "AudioBackup", "Audio", 
                    "VideoPlaylistItems", "VideoPlaylist", "VideoBackup", "Video", 
                    "ImageAlbumItems", "ImageAlbum", "Image", 
                    "EBookChapterPages", "EBookChapter", "EBookSeries", "EBook" 
                };

                foreach (var table in tables)
                {
                    try
                    {
                        var cmd = connection.CreateCommand();
                        cmd.CommandText = $"DELETE FROM {table}";
                        await cmd.ExecuteNonQueryAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error clearing table {Table}", table);
                    }
                }
                
                _logger.LogInformation("All records cleared from media database.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to clear media database");
            }
        }
    }
}
