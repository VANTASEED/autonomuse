using Autonomuse.Domain.Entities;
using Autonomuse.Infrastructure.Data;
using Autonomuse.Shared.Contracts;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Autonomuse.Services.Orchestration
{
    public class ImageService : IImageService
    {
        private readonly MediaDatabaseService _mediaDb;
        private readonly ISettingsService _settingsService;
        private readonly ILogger<ImageService> _logger;

        private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp"
        };

        public ImageService(MediaDatabaseService mediaDb, ISettingsService settingsService, ILogger<ImageService> logger)
        {
            _mediaDb = mediaDb;
            _settingsService = settingsService;
            _logger = logger;
        }

        public async Task<ImageRecord> AddImageAsync(string sourceFilePath, string source, ulong? perceptualHash = null)
        {
            var fileName = Path.GetFileName(sourceFilePath);
            var extension = Path.GetExtension(sourceFilePath).ToLowerInvariant();
            var title = Path.GetFileNameWithoutExtension(sourceFilePath);

            if (!SupportedExtensions.Contains(extension))
                throw new InvalidOperationException($"Unsupported image format: {extension}");

            var libraryPath = await _settingsService.GetSettingAsync("LibraryPath")
                ?? throw new InvalidOperationException("Library path not configured.");

            var imgFolder = Path.Combine(libraryPath, "Images");
            if (!Directory.Exists(imgFolder)) Directory.CreateDirectory(imgFolder);

            var destinationPath = Path.Combine(imgFolder, fileName);
            if (File.Exists(destinationPath))
            {
                var seg = Guid.NewGuid().ToString()[..8];
                destinationPath = Path.Combine(imgFolder, $"{title}_{seg}{extension}");
                fileName = Path.GetFileName(destinationPath);
            }

            File.Copy(sourceFilePath, destinationPath, false);
            var fileInfo = new FileInfo(destinationPath);

            var record = new ImageRecord
            {
                GUID = Guid.NewGuid().ToString(),
                FileName = fileName, Title = title, Extension = extension,
                Source = source, FilePath = destinationPath,
                FileSize = fileInfo.Length, PerceptualHash = perceptualHash,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
            };

            using var conn = new SqliteConnection(_mediaDb.GetConnectionString());
            await conn.OpenAsync();
            var cmd = conn.CreateCommand();
            cmd.CommandText = @"INSERT INTO Image ([GUID],[FileName],[Title],[Extension],[Source],[FilePath],[Width],[Height],[FileSize],[PerceptualHash],[CreatedAt],[UpdatedAt])
                VALUES (@g,@fn,@t,@ext,@src,@fp,@w,@h,@fs,@ph,@ca,@ua)";
            cmd.Parameters.AddWithValue("@g", record.GUID);
            cmd.Parameters.AddWithValue("@fn", record.FileName);
            cmd.Parameters.AddWithValue("@t", record.Title);
            cmd.Parameters.AddWithValue("@ext", record.Extension);
            cmd.Parameters.AddWithValue("@src", record.Source);
            cmd.Parameters.AddWithValue("@fp", record.FilePath);
            cmd.Parameters.AddWithValue("@w", (object?)record.Width ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@h", (object?)record.Height ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@fs", (object?)record.FileSize ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ph", (object?)unchecked((long?)record.PerceptualHash) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ca", record.CreatedAt);
            cmd.Parameters.AddWithValue("@ua", record.UpdatedAt);
            await cmd.ExecuteNonQueryAsync();

            _logger.LogInformation("Added image: {Title}", record.Title);
            return record;
        }

        public async Task<List<ImageRecord>> GetAllImagesAsync()
        {
            var records = new List<ImageRecord>();
            using var conn = new SqliteConnection(_mediaDb.GetConnectionString());
            await conn.OpenAsync();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT [GUID],[FileName],[Title],[Extension],[Source],[FilePath],[FileSize],[PerceptualHash],[CreatedAt],[UpdatedAt] FROM Image ORDER BY [CreatedAt] DESC";
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                records.Add(new ImageRecord
                {
                    GUID = r.GetString(0), FileName = r.GetString(1), Title = r.GetString(2),
                    Extension = r.GetString(3), Source = r.GetString(4), FilePath = r.GetString(5),
                    FileSize = r.IsDBNull(6) ? null : r.GetInt64(6),
                    PerceptualHash = r.IsDBNull(7) ? null : unchecked((ulong)r.GetInt64(7)),
                    CreatedAt = r.GetDateTime(8), UpdatedAt = r.GetDateTime(9)
                });
            }
            return records;
        }

        public async Task<List<ImageAlbum>> GetAlbumsAsync()
        {
            var list = new List<ImageAlbum>();
            using var conn = new SqliteConnection(_mediaDb.GetConnectionString());
            await conn.OpenAsync();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT [GUID],[Name],[Description],[CoverImageGUID],[CreatedAt],[UpdatedAt] FROM ImageAlbum ORDER BY [Name]";
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                list.Add(new ImageAlbum
                {
                    GUID = r.GetString(0), Name = r.GetString(1),
                    Description = r.IsDBNull(2) ? null : r.GetString(2),
                    CoverImageGUID = r.IsDBNull(3) ? null : r.GetString(3),
                    CreatedAt = r.GetDateTime(4), UpdatedAt = r.GetDateTime(5)
                });
            }
            return list;
        }

        public async Task<ImageAlbum> CreateAlbumAsync(string name)
        {
            var album = new ImageAlbum { GUID = Guid.NewGuid().ToString(), Name = name };
            using var conn = new SqliteConnection(_mediaDb.GetConnectionString());
            await conn.OpenAsync();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO ImageAlbum ([GUID],[Name],[CreatedAt],[UpdatedAt]) VALUES (@g,@n,@c,@u)";
            cmd.Parameters.AddWithValue("@g", album.GUID);
            cmd.Parameters.AddWithValue("@n", album.Name);
            cmd.Parameters.AddWithValue("@c", album.CreatedAt);
            cmd.Parameters.AddWithValue("@u", album.UpdatedAt);
            await cmd.ExecuteNonQueryAsync();
            return album;
        }

        public async Task AddToAlbumAsync(string albumGuid, string imageGuid)
        {
            using var conn = new SqliteConnection(_mediaDb.GetConnectionString());
            await conn.OpenAsync();
            var countCmd = conn.CreateCommand();
            countCmd.CommandText = "SELECT COALESCE(MAX([OrderIndex]),-1)+1 FROM ImageAlbumItems WHERE [AlbumGUID]=@ag";
            countCmd.Parameters.AddWithValue("@ag", albumGuid);
            var idx = Convert.ToInt32(await countCmd.ExecuteScalarAsync());

            var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO ImageAlbumItems ([GUID],[AlbumGUID],[ImageGUID],[OrderIndex],[AddedAt]) VALUES (@g,@ag,@ig,@oi,@a)";
            cmd.Parameters.AddWithValue("@g", Guid.NewGuid().ToString());
            cmd.Parameters.AddWithValue("@ag", albumGuid);
            cmd.Parameters.AddWithValue("@ig", imageGuid);
            cmd.Parameters.AddWithValue("@oi", idx);
            cmd.Parameters.AddWithValue("@a", DateTime.UtcNow);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<List<ImageRecord>> GetImagesByTitleAndSourceAsync(string title, string source)
        {
            var records = new List<ImageRecord>();
            try
            {
                using var conn = new SqliteConnection(_mediaDb.GetConnectionString());
                await conn.OpenAsync();
                var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT [GUID],[FileName],[Title],[Extension],[Source],[FilePath],[FileSize],[PerceptualHash],[CreatedAt],[UpdatedAt] FROM Image WHERE [Title] = @title AND [Source] = @source COLLATE NOCASE";
                cmd.Parameters.AddWithValue("@title", title);
                cmd.Parameters.AddWithValue("@source", source);

                using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    records.Add(new ImageRecord
                    {
                        GUID = r.GetString(0), FileName = r.GetString(1), Title = r.GetString(2),
                        Extension = r.GetString(3), Source = r.GetString(4), FilePath = r.GetString(5),
                        FileSize = r.IsDBNull(6) ? null : r.GetInt64(6),
                        PerceptualHash = r.IsDBNull(7) ? null : unchecked((ulong)r.GetInt64(7)),
                        CreatedAt = r.GetDateTime(8), UpdatedAt = r.GetDateTime(9)
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting images by title {Title}", title);
            }
            return records;
        }

        public async Task<List<string>> GetAlbumsForImageAsync(string imageGuid)
        {
            var list = new List<string>();
            try
            {
                using var conn = new SqliteConnection(_mediaDb.GetConnectionString());
                await conn.OpenAsync();
                var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT [AlbumGUID] FROM ImageAlbumItems WHERE [ImageGUID] = @ig";
                cmd.Parameters.AddWithValue("@ig", imageGuid);

                using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    list.Add(r.GetString(0));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting albums for image {Guid}", imageGuid);
            }
            return list;
        }
    }
}
