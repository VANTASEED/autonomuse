using Autonomuse.Domain.Entities;

namespace Autonomuse.Shared.Contracts
{
    public interface IEBookService
    {
        Task<EBookRecord> AddEBookAsync(string sourceFilePath, string source, string? seriesGuid = null, string? chapterGuid = null);
        Task<List<EBookRecord>> GetAllEBooksAsync();
        Task<List<EBookSeries>> GetSeriesAsync();
        Task<EBookSeries> CreateSeriesAsync(string name);
        Task<EBookChapter> CreateChapterAsync(string seriesGuid, int chapterNumber, string title);
        Task<List<EBookChapter>> GetChaptersBySeriesAsync(string seriesGuid);
        Task AddPagesToChapterAsync(string chapterGuid, List<string> imageFilePaths);
    }
}
