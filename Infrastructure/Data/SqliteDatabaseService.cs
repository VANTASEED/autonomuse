using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Autonomuse.Infrastructure.Data
{
    public class SqliteDatabaseService
    {
        private readonly string _dbPath;
        private readonly ILogger<SqliteDatabaseService> _logger;

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
    }
}
