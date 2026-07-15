using System.Text.Json;
using Autonomuse.Domain.Entities;
using Autonomuse.Infrastructure.Data;
using Autonomuse.Shared.Contracts;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Autonomuse.Services.Orchestration
{
    public class AudioWatchService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly MediaDatabaseService _mediaDb;
        private readonly IExternalToolService _toolService;
        private readonly ILogger<AudioWatchService> _logger;
        private bool _isSyncing;

        public bool IsSyncing => _isSyncing;
        public string? LastSyncResult { get; private set; }

        public AudioWatchService(
            IServiceScopeFactory scopeFactory,
            MediaDatabaseService mediaDb,
            IExternalToolService toolService,
            ILogger<AudioWatchService> logger)
        {
            _scopeFactory = scopeFactory;
            _mediaDb = mediaDb;
            _toolService = toolService;
            _logger = logger;
        }

        // ── Sync ──────────────────────────────────────────────

        public async Task SyncAllAsync()
        {
            if (_isSyncing) return;
            _isSyncing = true;
            var lines = new List<string>();
            try
            {
                var playlists = await GetWatchedPlaylistsAsync();
                if (playlists.Count == 0)
                {
                    LastSyncResult = "No watched playlists.";
                    return;
                }

                using var scope = _scopeFactory.CreateScope();
                var audioService = scope.ServiceProvider.GetRequiredService<IAudioService>();

                foreach (var wp in playlists)
                {
                    try
                    {
                        var result = await SyncOneAsync(wp, audioService);
                        lines.Add(result);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Sync failed for {Url}", wp.Url);
                        await MarkInvalidAsync(wp.GUID, ex.Message);
                        lines.Add($"{wp.PlaylistName ?? wp.Url}: {ex.Message}");
                    }
                }

                LastSyncResult = string.Join("; ", lines);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Sync failed");
                LastSyncResult = $"Sync failed: {ex.Message}";
            }
            finally
            {
                _isSyncing = false;
            }
        }

        private async Task<string> SyncOneAsync(AudioWatchPlaylist wp, IAudioService audioService)
        {
            _logger.LogInformation("Syncing playlist: {Name} ({Url})", wp.PlaylistName ?? wp.Url, wp.Url);

            var metaArgs = $"--dump-json --flat-playlist --no-warnings \"{wp.Url}\"";
            var (code, stdout, stderr) = await _toolService.RunCommandAsync("yt-dlp", metaArgs);

            if (code != 0)
            {
                var err = string.IsNullOrWhiteSpace(stderr) ? "Playlist not found or inaccessible" : stderr.Trim();
                await MarkInvalidAsync(wp.GUID, err);
                return $"{wp.PlaylistName ?? wp.Url}: invalid - {err}";
            }

            await MarkValidAsync(wp.GUID);

            var jsonLines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var items = jsonLines
                .Select(line => JsonSerializer.Deserialize<YtDlpFlatItem>(line))
                .Where(x => x != null && !string.IsNullOrEmpty(x.id))
                .ToList();

            if (items.Count == 0)
            {
                await UpdateLastCheckedAsync(wp.GUID);
                return $"{wp.PlaylistName ?? wp.Url}: empty playlist";
            }

            // Extract playlist name from metadata and persist it
            var playlistName = items[0]?.playlist_title;
            if (!string.IsNullOrEmpty(playlistName))
            {
                await UpdatePlaylistNameAsync(wp.GUID, playlistName);
                wp.PlaylistName = playlistName;
            }

            // Find or create an AudioPlaylist for this watched playlist
            var playlist = await audioService.CreatePlaylistAsync(wp.PlaylistName ?? "YouTube Watch", wp.Url);

            var newItems = new List<YtDlpFlatItem>();
            foreach (var item in items)
            {
                var exists = await audioService.GetAudioByYoutubeIDAsync(item!.id!);
                if (exists == null) newItems.Add(item);
            }

            if (newItems.Count == 0)
            {
                await UpdateLastCheckedAsync(wp.GUID);
                return $"{wp.PlaylistName ?? wp.Url}: up to date ({items.Count} items)";
            }

            using var scope = _scopeFactory.CreateScope();
            var youtubeService = scope.ServiceProvider.GetRequiredService<IYoutubeService>();

            var success = 0;
            var errors = 0;
            foreach (var item in newItems)
            {
                try
                {
                    var itemUrl = $"https://www.youtube.com/watch?v={item.id}";
                    await youtubeService.DownloadAudioAsync(itemUrl, onProgress: null, manualPlaylistGuid: playlist.GUID);
                    success++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Failed to download {Id}: {Ex}", item.id, ex.Message);
                    errors++;
                }
            }

            await UpdateLastCheckedAsync(wp.GUID);
            return $"{wp.PlaylistName ?? wp.Url}: {success} new, {errors} errors";
        }

        // ── CRUD ──────────────────────────────────────────────

        public async Task<List<AudioWatchPlaylist>> GetWatchedPlaylistsAsync()
        {
            var list = new List<AudioWatchPlaylist>();
            try
            {
                using var connection = new SqliteConnection(_mediaDb.GetConnectionString());
                await connection.OpenAsync();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT [GUID], [Url], [PlaylistName], [CreatedAt], [LastCheckedAt], [IsValid], [LastError] FROM AudioWatchPlaylist ORDER BY [CreatedAt] DESC";
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    list.Add(new AudioWatchPlaylist
                    {
                        GUID = reader.GetString(0),
                        Url = reader.GetString(1),
                        PlaylistName = reader.IsDBNull(2) ? null : reader.GetString(2),
                        CreatedAt = reader.GetDateTime(3),
                        LastCheckedAt = reader.IsDBNull(4) ? null : reader.GetDateTime(4),
                        IsValid = reader.GetInt32(5) == 1,
                        LastError = reader.IsDBNull(6) ? null : reader.GetString(6)
                    });
                }
            }
            catch (Exception ex) { _logger.LogError(ex, "Error getting watched playlists"); }
            return list;
        }

        public async Task AddWatchAsync(string url)
        {
            try
            {
                var existing = await GetWatchedPlaylistsAsync();
                if (existing.Any(w => w.Url.Equals(url, StringComparison.OrdinalIgnoreCase)))
                {
                    _logger.LogInformation("Playlist already watched: {Url}", url);
                    return;
                }

                var entity = new AudioWatchPlaylist
                {
                    GUID = Guid.NewGuid().ToString(),
                    Url = url,
                    CreatedAt = DateTime.UtcNow
                };

                using var connection = new SqliteConnection(_mediaDb.GetConnectionString());
                await connection.OpenAsync();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "INSERT INTO AudioWatchPlaylist ([GUID], [Url], [PlaylistName], [CreatedAt], [IsValid]) VALUES (@g, @u, @n, @c, 1)";
                cmd.Parameters.AddWithValue("@g", entity.GUID);
                cmd.Parameters.AddWithValue("@u", entity.Url);
                cmd.Parameters.AddWithValue("@n", (object?)entity.PlaylistName ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@c", entity.CreatedAt);
                await cmd.ExecuteNonQueryAsync();
                _logger.LogInformation("Added watched playlist: {Url}", url);
            }
            catch (Exception ex) { _logger.LogError(ex, "Error adding watched playlist"); }
        }

        public async Task RemoveWatchAsync(string guid)
        {
            try
            {
                using var connection = new SqliteConnection(_mediaDb.GetConnectionString());
                await connection.OpenAsync();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "DELETE FROM AudioWatchPlaylist WHERE [GUID] = @g";
                cmd.Parameters.AddWithValue("@g", guid);
                await cmd.ExecuteNonQueryAsync();
                _logger.LogInformation("Removed watched playlist: {GUID}", guid);
            }
            catch (Exception ex) { _logger.LogError(ex, "Error removing watched playlist"); }
        }

        public async Task UpdateWatchUrlAsync(string guid, string newUrl)
        {
            try
            {
                using var connection = new SqliteConnection(_mediaDb.GetConnectionString());
                await connection.OpenAsync();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "UPDATE AudioWatchPlaylist SET [Url] = @u, [IsValid] = 1, [LastError] = NULL WHERE [GUID] = @g";
                cmd.Parameters.AddWithValue("@u", newUrl);
                cmd.Parameters.AddWithValue("@g", guid);
                await cmd.ExecuteNonQueryAsync();
                _logger.LogInformation("Updated watch playlist URL: {GUID} -> {Url}", guid, newUrl);
            }
            catch (Exception ex) { _logger.LogError(ex, "Error updating watch URL"); }
        }

        // ── Internal helpers ──────────────────────────────────

        private async Task UpdateLastCheckedAsync(string guid)
        {
            try
            {
                using var connection = new SqliteConnection(_mediaDb.GetConnectionString());
                await connection.OpenAsync();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "UPDATE AudioWatchPlaylist SET [LastCheckedAt] = @d WHERE [GUID] = @g";
                cmd.Parameters.AddWithValue("@d", DateTime.UtcNow);
                cmd.Parameters.AddWithValue("@g", guid);
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex) { _logger.LogError(ex, "Error updating last checked"); }
        }

        private async Task MarkInvalidAsync(string guid, string error)
        {
            try
            {
                using var connection = new SqliteConnection(_mediaDb.GetConnectionString());
                await connection.OpenAsync();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "UPDATE AudioWatchPlaylist SET [IsValid] = 0, [LastError] = @e WHERE [GUID] = @g";
                cmd.Parameters.AddWithValue("@e", error);
                cmd.Parameters.AddWithValue("@g", guid);
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex) { _logger.LogError(ex, "Error marking playlist invalid"); }
        }

        private async Task MarkValidAsync(string guid)
        {
            try
            {
                using var connection = new SqliteConnection(_mediaDb.GetConnectionString());
                await connection.OpenAsync();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "UPDATE AudioWatchPlaylist SET [IsValid] = 1, [LastError] = NULL WHERE [GUID] = @g";
                cmd.Parameters.AddWithValue("@g", guid);
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex) { _logger.LogError(ex, "Error marking playlist valid"); }
        }

        private async Task UpdatePlaylistNameAsync(string guid, string name)
        {
            try
            {
                using var connection = new SqliteConnection(_mediaDb.GetConnectionString());
                await connection.OpenAsync();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "UPDATE AudioWatchPlaylist SET [PlaylistName] = @n WHERE [GUID] = @g";
                cmd.Parameters.AddWithValue("@n", name);
                cmd.Parameters.AddWithValue("@g", guid);
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex) { _logger.LogError(ex, "Error updating playlist name"); }
        }

        // ── DTO ───────────────────────────────────────────────

        private class YtDlpFlatItem
        {
            public string? id { get; set; }
            public string? title { get; set; }
            public string? playlist_title { get; set; }
        }
    }
}
