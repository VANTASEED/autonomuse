using System.Text.Json;
using Autonomuse.Domain.Entities;
using Autonomuse.Infrastructure.Data;
using Autonomuse.Shared.Contracts;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace Autonomuse.Services.Orchestration
{
    public class VideoWatchService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly MediaDatabaseService _mediaDb;
        private readonly IExternalToolService _toolService;
        private readonly ILogger<VideoWatchService> _logger;
        private bool _isSyncing;

        public bool IsSyncing => _isSyncing;

        public VideoWatchService(
            IServiceScopeFactory scopeFactory,
            MediaDatabaseService mediaDb,
            IExternalToolService toolService,
            ILogger<VideoWatchService> logger)
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
            try
            {
                var playlists = await GetWatchedPlaylistsAsync();
                if (playlists.Count == 0) return;

                using var scope = _scopeFactory.CreateScope();
                var videoService = scope.ServiceProvider.GetRequiredService<IVideoService>();

                foreach (var wp in playlists)
                {
                    try
                    {
                        await SyncOneAsync(wp, videoService);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Sync failed for {Url}", wp.Url);
                        await UpdateLastStatusAsync(wp.GUID, $"Sync failed: {ex.Message}");
                        await MarkInvalidAsync(wp.GUID, ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Sync failed");
            }
            finally
            {
                _isSyncing = false;
            }
        }

        private async Task SyncOneAsync(VideoWatchPlaylist wp, IVideoService videoService)
        {
            _logger.LogInformation("Syncing playlist: {Name} ({Url})", wp.PlaylistName ?? wp.Url, wp.Url);

            var metaArgs = $"--dump-json --flat-playlist --no-warnings \"{wp.Url}\"";
            var (code, stdout, stderr) = await _toolService.RunCommandAsync("yt-dlp", metaArgs);

            if (code != 0)
            {
                var err = string.IsNullOrWhiteSpace(stderr) ? "Playlist not found or inaccessible" : stderr.Trim();
                await MarkInvalidAsync(wp.GUID, err);
                await UpdateLastStatusAsync(wp.GUID, $"Invalid - {err}");
                return;
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
                await UpdateLastStatusAsync(wp.GUID, "Empty playlist");
                return;
            }

            // Extract playlist name from metadata and persist it
            var playlistName = items[0]?.playlist_title;
            if (!string.IsNullOrEmpty(playlistName))
            {
                await UpdatePlaylistNameAsync(wp.GUID, playlistName);
                wp.PlaylistName = playlistName;
            }

            // Find or create a VideoPlaylist for this watched playlist
            var playlist = await videoService.CreatePlaylistAsync(wp.PlaylistName ?? "YouTube Watch", wp.Url);

            // Prune items that are no longer in the YouTube playlist
            var currentYtIds = items.Select(i => i!.id!).ToHashSet();
            var localItems = await videoService.GetVideoInPlaylistAsync(playlist.GUID);
            var removedCount = 0;
            foreach (var localItem in localItems)
            {
                if (!string.IsNullOrEmpty(localItem.YoutubeID) && !currentYtIds.Contains(localItem.YoutubeID))
                {
                    await videoService.RemoveFromPlaylistAsync(playlist.GUID, localItem.GUID);
                    removedCount++;
                }
            }

            var newItems = new List<YtDlpFlatItem>();
            foreach (var item in items)
            {
                var exists = await videoService.GetVideoByYoutubeIDAsync(item!.id!);
                if (exists == null) newItems.Add(item);
            }

            if (newItems.Count == 0)
            {
                await UpdateLastCheckedAsync(wp.GUID);
                await UpdateLastStatusAsync(wp.GUID, $"Up to date ({items.Count} items), {removedCount} removed from playlist");
                return;
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
                    await youtubeService.DownloadVideoAsync(itemUrl, onProgress: null, manualPlaylistGuid: playlist.GUID);
                    success++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Failed to download {Id}: {Ex}", item.id, ex.Message);
                    errors++;
                }
            }

            await UpdateLastCheckedAsync(wp.GUID);
            await UpdateLastStatusAsync(wp.GUID, $"{success} new, {errors} errors, {removedCount} removed from playlist");
        }

        // ── CRUD ──────────────────────────────────────────────

        public async Task<List<VideoWatchPlaylist>> GetWatchedPlaylistsAsync()
        {
            var list = new List<VideoWatchPlaylist>();
            try
            {
                using var connection = new SqliteConnection(_mediaDb.GetConnectionString());
                await connection.OpenAsync();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT [GUID], [Url], [PlaylistName], [CreatedAt], [LastCheckedAt], [IsValid], [LastError], [LastStatus] FROM VideoWatchPlaylist ORDER BY [CreatedAt] DESC";
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    list.Add(new VideoWatchPlaylist
                    {
                        GUID = reader.GetString(0),
                        Url = reader.GetString(1),
                        PlaylistName = reader.IsDBNull(2) ? null : reader.GetString(2),
                        CreatedAt = reader.GetDateTime(3),
                        LastCheckedAt = reader.IsDBNull(4) ? null : reader.GetDateTime(4),
                        IsValid = reader.GetInt32(5) == 1,
                        LastError = reader.IsDBNull(6) ? null : reader.GetString(6),
                        LastStatus = reader.IsDBNull(7) ? null : reader.GetString(7)
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

                var entity = new VideoWatchPlaylist
                {
                    GUID = Guid.NewGuid().ToString(),
                    Url = url,
                    CreatedAt = DateTime.UtcNow
                };

                using var connection = new SqliteConnection(_mediaDb.GetConnectionString());
                await connection.OpenAsync();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "INSERT INTO VideoWatchPlaylist ([GUID], [Url], [PlaylistName], [CreatedAt], [IsValid]) VALUES (@g, @u, @n, @c, 1)";
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
                cmd.CommandText = "DELETE FROM VideoWatchPlaylist WHERE [GUID] = @g";
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
                cmd.CommandText = "UPDATE VideoWatchPlaylist SET [Url] = @u, [IsValid] = 1, [LastError] = NULL WHERE [GUID] = @g";
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
                cmd.CommandText = "UPDATE VideoWatchPlaylist SET [LastCheckedAt] = @d WHERE [GUID] = @g";
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
                cmd.CommandText = "UPDATE VideoWatchPlaylist SET [IsValid] = 0, [LastError] = @e WHERE [GUID] = @g";
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
                cmd.CommandText = "UPDATE VideoWatchPlaylist SET [IsValid] = 1, [LastError] = NULL WHERE [GUID] = @g";
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
                cmd.CommandText = "UPDATE VideoWatchPlaylist SET [PlaylistName] = @n WHERE [GUID] = @g";
                cmd.Parameters.AddWithValue("@n", name);
                cmd.Parameters.AddWithValue("@g", guid);
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex) { _logger.LogError(ex, "Error updating playlist name"); }
        }

        private async Task UpdateLastStatusAsync(string guid, string status)
        {
            try
            {
                using var connection = new SqliteConnection(_mediaDb.GetConnectionString());
                await connection.OpenAsync();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "UPDATE VideoWatchPlaylist SET [LastStatus] = @s WHERE [GUID] = @g";
                cmd.Parameters.AddWithValue("@s", status);
                cmd.Parameters.AddWithValue("@g", guid);
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex) { _logger.LogError(ex, "Error updating last status"); }
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
