using Autonomuse.Domain.Entities;
using Autonomuse.Infrastructure.Data;
using Autonomuse.Shared.Contracts;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Autonomuse.Services.Orchestration
{
    public class EBookService : IEBookService
    {
        private readonly MediaDatabaseService _mediaDb;
        private readonly ISettingsService _settingsService;
        private readonly ILogger<EBookService> _logger;

        // Standard e-book formats
        private static readonly HashSet<string> BookExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf", ".epub", ".cbz", ".cbr", ".mobi"
        };

        // Manga page images (also accepted under e-books context)
        private static readonly HashSet<string> MangaPageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png"
        };

        public static HashSet<string> AllSupportedExtensions
        {
            get
            {
                var all = new HashSet<string>(BookExtensions, StringComparer.OrdinalIgnoreCase);
                foreach (var ext in MangaPageExtensions) all.Add(ext);
                return all;
            }
        }

        public EBookService(MediaDatabaseService mediaDb, ISettingsService settingsService, ILogger<EBookService> logger)
        {
            _mediaDb = mediaDb;
            _settingsService = settingsService;
            _logger = logger;
        }

        public async Task<EBookRecord> AddEBookAsync(string sourceFilePath, string source, string? seriesGuid = null, string? chapterGuid = null)
        {
            var originalFileName = Path.GetFileName(sourceFilePath);
            var extension = Path.GetExtension(sourceFilePath).ToLowerInvariant();
            var title = Path.GetFileNameWithoutExtension(sourceFilePath);

            if (!BookExtensions.Contains(extension) && !MangaPageExtensions.Contains(extension))
                throw new InvalidOperationException($"Unsupported e-book format: {extension}");

            var libraryPath = await _settingsService.GetSettingAsync("LibraryPath")
                ?? throw new InvalidOperationException("Library path not configured.");

            var baseBookFolder = Path.Combine(libraryPath, "EBooks");
            var targetFolder = baseBookFolder;

            // Resolve Series and Chapter folders if GUIDs are provided
            string? resolvedSeriesGuid = seriesGuid;
            if (!string.IsNullOrEmpty(chapterGuid) && string.IsNullOrEmpty(resolvedSeriesGuid))
            {
                // Find SeriesGUID from Chapter
                using var conn = new SqliteConnection(_mediaDb.GetConnectionString());
                await conn.OpenAsync();
                var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT [SeriesGUID] FROM EBookChapter WHERE [GUID] = @g";
                cmd.Parameters.AddWithValue("@g", chapterGuid);
                resolvedSeriesGuid = (await cmd.ExecuteScalarAsync())?.ToString();
            }

            if (!string.IsNullOrEmpty(resolvedSeriesGuid))
            {
                using var conn = new SqliteConnection(_mediaDb.GetConnectionString());
                await conn.OpenAsync();
                var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT [Name] FROM EBookSeries WHERE [GUID] = @g";
                cmd.Parameters.AddWithValue("@g", resolvedSeriesGuid);
                var seriesName = (await cmd.ExecuteScalarAsync())?.ToString() ?? "Unknown Series";
                targetFolder = Path.Combine(targetFolder, seriesName);
            }

            if (!string.IsNullOrEmpty(chapterGuid))
            {
                using var conn = new SqliteConnection(_mediaDb.GetConnectionString());
                await conn.OpenAsync();
                var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT [Title] FROM EBookChapter WHERE [GUID] = @g";
                cmd.Parameters.AddWithValue("@g", chapterGuid);
                var chapterTitle = (await cmd.ExecuteScalarAsync())?.ToString() ?? "Unknown Chapter";
                targetFolder = Path.Combine(targetFolder, chapterTitle);
            }

            if (!Directory.Exists(targetFolder)) Directory.CreateDirectory(targetFolder);

            // Rename scheme: page1_ddMMyy_hhmmss.jpg
            var timestamp = DateTime.Now.ToString("ddMMyy_HHmmss");
            var newFileName = $"{title}_{timestamp}{extension}";
            var destinationPath = Path.Combine(targetFolder, newFileName);

            // If collision still exists (rare with timestamp), add GUID segment
            if (File.Exists(destinationPath))
            {
                var seg = Guid.NewGuid().ToString()[..4];
                newFileName = $"{title}_{timestamp}_{seg}{extension}";
                destinationPath = Path.Combine(targetFolder, newFileName);
            }

            File.Copy(sourceFilePath, destinationPath, false);
            var fileInfo = new FileInfo(destinationPath);

            var record = new EBookRecord
            {
                GUID = Guid.NewGuid().ToString(),
                FileName = newFileName, Title = title, Extension = extension,
                Source = source, FilePath = destinationPath,
                FileSize = fileInfo.Length,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
            };

            using var conn2 = new SqliteConnection(_mediaDb.GetConnectionString());
            await conn2.OpenAsync();
            var cmd2 = conn2.CreateCommand();
            cmd2.CommandText = @"INSERT INTO EBook ([GUID],[FileName],[Title],[Extension],[Source],[FilePath],[Author],[PageCount],[FileSize],[CoverPath],[CreatedAt],[UpdatedAt])
                VALUES (@g,@fn,@t,@ext,@src,@fp,@au,@pc,@fs,@cp,@ca,@ua)";
            cmd2.Parameters.AddWithValue("@g", record.GUID);
            cmd2.Parameters.AddWithValue("@fn", record.FileName);
            cmd2.Parameters.AddWithValue("@t", record.Title);
            cmd2.Parameters.AddWithValue("@ext", record.Extension);
            cmd2.Parameters.AddWithValue("@src", record.Source);
            cmd2.Parameters.AddWithValue("@fp", record.FilePath);
            cmd2.Parameters.AddWithValue("@au", (object?)record.Author ?? DBNull.Value);
            cmd2.Parameters.AddWithValue("@pc", (object?)record.PageCount ?? DBNull.Value);
            cmd2.Parameters.AddWithValue("@fs", (object?)record.FileSize ?? DBNull.Value);
            cmd2.Parameters.AddWithValue("@cp", (object?)record.CoverPath ?? DBNull.Value);
            cmd2.Parameters.AddWithValue("@ca", record.CreatedAt);
            cmd2.Parameters.AddWithValue("@ua", record.UpdatedAt);
            await cmd2.ExecuteNonQueryAsync();

            _logger.LogInformation("Added e-book: {Title} at {Path}", record.Title, record.FilePath);
            return record;
        }

        public async Task<List<EBookRecord>> GetAllEBooksAsync()
        {
            var records = new List<EBookRecord>();
            using var conn = new SqliteConnection(_mediaDb.GetConnectionString());
            await conn.OpenAsync();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT [GUID],[FileName],[Title],[Extension],[Source],[FilePath],[FileSize],[CreatedAt],[UpdatedAt] FROM EBook ORDER BY [CreatedAt] DESC";
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                records.Add(new EBookRecord
                {
                    GUID = r.GetString(0), FileName = r.GetString(1), Title = r.GetString(2),
                    Extension = r.GetString(3), Source = r.GetString(4), FilePath = r.GetString(5),
                    FileSize = r.IsDBNull(6) ? null : r.GetInt64(6),
                    CreatedAt = r.GetDateTime(7), UpdatedAt = r.GetDateTime(8)
                });
            }
            return records;
        }

        public async Task<List<EBookSeries>> GetSeriesAsync()
        {
            var list = new List<EBookSeries>();
            using var conn = new SqliteConnection(_mediaDb.GetConnectionString());
            await conn.OpenAsync();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT [GUID],[Name],[Description],[CreatedAt],[UpdatedAt] FROM EBookSeries ORDER BY [Name]";
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                list.Add(new EBookSeries
                {
                    GUID = r.GetString(0), Name = r.GetString(1),
                    Description = r.IsDBNull(2) ? null : r.GetString(2),
                    CreatedAt = r.GetDateTime(3), UpdatedAt = r.GetDateTime(4)
                });
            }
            return list;
        }

        public async Task<EBookSeries> CreateSeriesAsync(string name)
        {
            var series = new EBookSeries { GUID = Guid.NewGuid().ToString(), Name = name };
            using var conn = new SqliteConnection(_mediaDb.GetConnectionString());
            await conn.OpenAsync();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO EBookSeries ([GUID],[Name],[CreatedAt],[UpdatedAt]) VALUES (@g,@n,@c,@u)";
            cmd.Parameters.AddWithValue("@g", series.GUID);
            cmd.Parameters.AddWithValue("@n", series.Name);
            cmd.Parameters.AddWithValue("@c", series.CreatedAt);
            cmd.Parameters.AddWithValue("@u", series.UpdatedAt);
            await cmd.ExecuteNonQueryAsync();
            return series;
        }

        public async Task<EBookChapter> CreateChapterAsync(string seriesGuid, int chapterNumber, string title)
        {
            var chapter = new EBookChapter
            {
                GUID = Guid.NewGuid().ToString(),
                SeriesGUID = seriesGuid,
                ChapterNumber = chapterNumber,
                Title = title
            };
            using var conn = new SqliteConnection(_mediaDb.GetConnectionString());
            await conn.OpenAsync();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO EBookChapter ([GUID],[SeriesGUID],[ChapterNumber],[Title],[CreatedAt],[UpdatedAt]) VALUES (@g,@sg,@cn,@t,@c,@u)";
            cmd.Parameters.AddWithValue("@g", chapter.GUID);
            cmd.Parameters.AddWithValue("@sg", chapter.SeriesGUID);
            cmd.Parameters.AddWithValue("@cn", chapter.ChapterNumber);
            cmd.Parameters.AddWithValue("@t", chapter.Title);
            cmd.Parameters.AddWithValue("@c", chapter.CreatedAt);
            cmd.Parameters.AddWithValue("@u", chapter.UpdatedAt);
            await cmd.ExecuteNonQueryAsync();
            return chapter;
        }

        public async Task<List<EBookChapter>> GetChaptersBySeriesAsync(string seriesGuid)
        {
            var list = new List<EBookChapter>();
            using var conn = new SqliteConnection(_mediaDb.GetConnectionString());
            await conn.OpenAsync();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT [GUID],[SeriesGUID],[ChapterNumber],[Title],[CreatedAt],[UpdatedAt] FROM EBookChapter WHERE [SeriesGUID]=@sg ORDER BY [ChapterNumber]";
            cmd.Parameters.AddWithValue("@sg", seriesGuid);
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                list.Add(new EBookChapter
                {
                    GUID = r.GetString(0),
                    SeriesGUID = r.GetString(1),
                    ChapterNumber = r.GetInt32(2),
                    Title = r.GetString(3),
                    CreatedAt = r.GetDateTime(4),
                    UpdatedAt = r.GetDateTime(5)
                });
            }
            return list;
        }

        public async Task AddPagesToChapterAsync(string chapterGuid, List<string> imageFilePaths)
        {
            for (int i = 0; i < imageFilePaths.Count; i++)
            {
                // Each page image is stored as an EBook record, then linked to the chapter
                var pageRecord = await AddEBookAsync(imageFilePaths[i], "manual upload", null, chapterGuid);

                using var conn = new SqliteConnection(_mediaDb.GetConnectionString());
                await conn.OpenAsync();
                var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO EBookChapterPages ([GUID],[ChapterGUID],[EBookGUID],[PageIndex],[AddedAt]) VALUES (@g,@cg,@eg,@pi,@a)";
                cmd.Parameters.AddWithValue("@g", Guid.NewGuid().ToString());
                cmd.Parameters.AddWithValue("@cg", chapterGuid);
                cmd.Parameters.AddWithValue("@eg", pageRecord.GUID);
                cmd.Parameters.AddWithValue("@pi", i);
                cmd.Parameters.AddWithValue("@a", DateTime.UtcNow);
                await cmd.ExecuteNonQueryAsync();
            }
        }
    }
}
