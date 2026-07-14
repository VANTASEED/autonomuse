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
            var dbFolder = Path.Combine(localAppData, "Autonomuse", "com.vantaseed.autonomuse", "Data");

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

                ";
                command.ExecuteNonQuery();
                var verifyCmd = connection.CreateCommand();
                verifyCmd.CommandText = @"
                    SELECT count(*) FROM sqlite_master 
                    WHERE type='table' AND name IN (
                        'Audio', 'AudioPlaylist', 'AudioPlaylistItems',
                        'Video', 'VideoPlaylist', 'VideoPlaylistItems'
                    );
                ";
                var tableCount = Convert.ToInt32(verifyCmd.ExecuteScalar());

                if (tableCount == 8)
                {
                    _logger.LogInformation("Media database verified successfully at {Path}. All 8 tables present.", _dbPath);
                }
                else
                {
                    _logger.LogError("Media database initialization incomplete. Expected 8 tables, found {Count}", tableCount);
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
                    "VideoPlaylistItems", "VideoPlaylist", "VideoBackup", "Video" 
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
