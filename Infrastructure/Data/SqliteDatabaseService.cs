using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Autonomuse.Infrastructure.Data
{
    public class SqliteDatabaseService
    {
        private readonly string _dbPath;
        private readonly ILogger<SqliteDatabaseService> _logger;
        private bool _isDebugMode = false;
        private bool _isImageFeatureEnabled = false;
        private bool _isEbookFeatureEnabled = false;

        public bool IsDebugMode => _isDebugMode;
        public bool IsImageFeatureEnabled => _isImageFeatureEnabled;
        public bool IsEbookFeatureEnabled => _isEbookFeatureEnabled;

        public SqliteDatabaseService(ILogger<SqliteDatabaseService> logger)
        {
            _logger = logger;
            
            // Per User request: Local/Autonomuse/com.reversedhorizonstudio.autonomuse/Data/setting.db
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var dbFolder = Path.Combine(localAppData, "Autonomuse", "com.reversedhorizonstudio.autonomuse", "Data");
            
            if (!Directory.Exists(dbFolder))
            {
                Directory.CreateDirectory(dbFolder);
            }

            _dbPath = Path.Combine(dbFolder, "setting.db");
            InitializeDatabase();
        }

        public string GetConnectionString() => $"Data Source={_dbPath}";

        private void InitializeDatabase()
        {
            try
            {
                using var connection = new SqliteConnection(GetConnectionString());
                connection.Open();

                // Escaping identifiers to avoid keyword conflicts (like "Values")
                var command = connection.CreateCommand();
                command.CommandText = @"
                    CREATE TABLE IF NOT EXISTS Settings (
                        [Parameter] TEXT PRIMARY KEY,
                        [Values] TEXT
                    );

                    CREATE TABLE IF NOT EXISTS PathHistory (
                        [Path] TEXT PRIMARY KEY,
                        [LastChangedDate] DATETIME
                    );
                ";
                command.ExecuteNonQuery();

                // Verification check
                var verifyCmd = connection.CreateCommand();
                verifyCmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='Settings';";
                var tableCount = Convert.ToInt32(verifyCmd.ExecuteScalar());

                if (tableCount > 0)
                {
                    _logger.LogInformation("Database and Settings table verified successfully at {Path}", _dbPath);
                }
                else
                {
                    _logger.LogError("Table 'Settings' was not found after initialization!");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize database");
            }
        }
        public async Task ResetPersonalizationAsync()
        {
            try
            {
                using var connection = new SqliteConnection(GetConnectionString());
                await connection.OpenAsync();

                var command = connection.CreateCommand();
                command.CommandText = "DELETE FROM Settings WHERE [Parameter] IN ('HomeUISettings', 'UserAccents') OR [Parameter] LIKE 'Background_%'";
                await command.ExecuteNonQueryAsync();
                
                _logger.LogInformation("Personalization settings reset in database.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reset personalization settings in database");
            }
        }
        public async Task InitializeCoreSettingsAsync()
        {
            try
            {
                using var connection = new SqliteConnection(GetConnectionString());
                await connection.OpenAsync();

                // 1. Initialize DebugMode
                var debugCmd = connection.CreateCommand();
                debugCmd.CommandText = "SELECT [Values] FROM Settings WHERE [Parameter] = 'DebugMode'";
                var debugResult = await debugCmd.ExecuteScalarAsync();

                if (debugResult == null)
                {
                    var insertCmd = connection.CreateCommand();
                    insertCmd.CommandText = "INSERT INTO Settings ([Parameter], [Values]) VALUES ('DebugMode', 'false')";
                    await insertCmd.ExecuteNonQueryAsync();
                    _isDebugMode = false;
                }
                else
                {
                    _isDebugMode = debugResult?.ToString()?.ToLower() == "true";
                }

                // 1b. Initialize ImageFeature
                var imageFeatureCmd = connection.CreateCommand();
                imageFeatureCmd.CommandText = "SELECT [Values] FROM Settings WHERE [Parameter] = 'ImageFeature'";
                var imageFeatureResult = await imageFeatureCmd.ExecuteScalarAsync();

                if (imageFeatureResult == null)
                {
                    var insertCmd = connection.CreateCommand();
                    insertCmd.CommandText = "INSERT INTO Settings ([Parameter], [Values]) VALUES ('ImageFeature', 'false')";
                    await insertCmd.ExecuteNonQueryAsync();
                    _isImageFeatureEnabled = false;
                }
                else
                {
                    _isImageFeatureEnabled = imageFeatureResult?.ToString()?.ToLower() == "true";
                }

                // 1c. Initialize EbookFeature
                var ebookFeatureCmd = connection.CreateCommand();
                ebookFeatureCmd.CommandText = "SELECT [Values] FROM Settings WHERE [Parameter] = 'EbookFeature'";
                var ebookFeatureResult = await ebookFeatureCmd.ExecuteScalarAsync();

                if (ebookFeatureResult == null)
                {
                    var insertCmd = connection.CreateCommand();
                    insertCmd.CommandText = "INSERT INTO Settings ([Parameter], [Values]) VALUES ('EbookFeature', 'false')";
                    await insertCmd.ExecuteNonQueryAsync();
                    _isEbookFeatureEnabled = false;
                }
                else
                {
                    _isEbookFeatureEnabled = ebookFeatureResult?.ToString()?.ToLower() == "true";
                }

                // 2. Initialize CoverArtQuality
                var qualityCmd = connection.CreateCommand();
                qualityCmd.CommandText = "SELECT COUNT(*) FROM Settings WHERE [Parameter] = 'CoverArtQuality'";
                var qualityExists = Convert.ToInt32(await qualityCmd.ExecuteScalarAsync()) > 0;

                if (!qualityExists)
                {
                    var insertCmd = connection.CreateCommand();
                    insertCmd.CommandText = "INSERT INTO Settings ([Parameter], [Values]) VALUES ('CoverArtQuality', 'standard')";
                    await insertCmd.ExecuteNonQueryAsync();
                }

                _logger.LogInformation("Core settings initialized (DebugMode: {Status})", _isDebugMode);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing core settings");
            }
        }
    }
}
