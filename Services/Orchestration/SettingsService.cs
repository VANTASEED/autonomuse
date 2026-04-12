using Autonomuse.Infrastructure.Data;
using Autonomuse.Shared.Contracts;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Autonomuse.Services.Orchestration
{
    public class SettingsService : ISettingsService
    {
        private readonly SqliteDatabaseService _dbService;
        private readonly ILogger<SettingsService> _logger;

        public SettingsService(SqliteDatabaseService dbService, ILogger<SettingsService> logger)
        {
            _dbService = dbService;
            _logger = logger;
        }

        public async Task<string?> GetSettingAsync(string parameter)
        {
            try
            {
                using var connection = new SqliteConnection(_dbService.GetConnectionString());
                await connection.OpenAsync();

                var command = connection.CreateCommand();
                command.CommandText = "SELECT [Values] FROM Settings WHERE [Parameter] = @param";
                command.Parameters.AddWithValue("@param", parameter);

                var result = await command.ExecuteScalarAsync();
                return result?.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving setting {Param}", parameter);
                return null;
            }
        }

        public async Task SaveSettingAsync(string parameter, string value)
        {
            try
            {
                using var connection = new SqliteConnection(_dbService.GetConnectionString());
                await connection.OpenAsync();

                var command = connection.CreateCommand();
                command.CommandText = @"
                    INSERT INTO Settings ([Parameter], [Values]) 
                    VALUES (@param, @val)
                    ON CONFLICT([Parameter]) DO UPDATE SET [Values] = @val;
                ";
                command.Parameters.AddWithValue("@param", parameter);
                command.Parameters.AddWithValue("@val", value);

                await command.ExecuteNonQueryAsync();
                _logger.LogInformation("Saved setting {Param} = {Val}", parameter, value);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving setting {Param}", parameter);
            }
        }

        public async Task<List<string>> GetPathHistoryAsync()
        {
            var history = new List<string>();
            try
            {
                using var connection = new SqliteConnection(_dbService.GetConnectionString());
                await connection.OpenAsync();

                var command = connection.CreateCommand();
                command.CommandText = "SELECT [Path] FROM PathHistory ORDER BY [LastChangedDate] DESC LIMIT 5";

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    history.Add(reader.GetString(0));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving path history");
            }
            return history;
        }

        public async Task AddPathHistoryAsync(string path)
        {
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                using var connection = new SqliteConnection(_dbService.GetConnectionString());
                await connection.OpenAsync();

                // 1. Insert or update the path with current date
                var insertCmd = connection.CreateCommand();
                insertCmd.CommandText = @"
                    INSERT INTO PathHistory ([Path], [LastChangedDate]) 
                    VALUES (@path, @date)
                    ON CONFLICT([Path]) DO UPDATE SET [LastChangedDate] = @date;
                ";
                insertCmd.Parameters.AddWithValue("@path", path);
                insertCmd.Parameters.AddWithValue("@date", DateTime.UtcNow);
                await insertCmd.ExecuteNonQueryAsync();

                // 2. Prune history to keep only 5 most recent
                var pruneCmd = connection.CreateCommand();
                pruneCmd.CommandText = @"
                    DELETE FROM PathHistory 
                    WHERE [Path] NOT IN (
                        SELECT [Path] FROM PathHistory 
                        ORDER BY [LastChangedDate] DESC 
                        LIMIT 5
                    );
                ";
                await pruneCmd.ExecuteNonQueryAsync();

                _logger.LogInformation("Added {Path} to history", path);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding path history");
            }
        }

        public async Task RemovePathHistoryAsync(string path)
        {
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                using var connection = new SqliteConnection(_dbService.GetConnectionString());
                await connection.OpenAsync();

                var command = connection.CreateCommand();
                command.CommandText = "DELETE FROM PathHistory WHERE [Path] = @path";
                command.Parameters.AddWithValue("@path", path);
                await command.ExecuteNonQueryAsync();

                _logger.LogInformation("Removed {Path} from history", path);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing path history");
            }
        }
    }
}
