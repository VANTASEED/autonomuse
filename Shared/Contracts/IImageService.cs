using Autonomuse.Domain.Entities;

namespace Autonomuse.Shared.Contracts
{
    public interface IImageService
    {
        Task<ImageRecord> AddImageAsync(string sourceFilePath, string source, ulong? perceptualHash = null);
        Task<List<ImageRecord>> GetAllImagesAsync();
        Task<List<ImageAlbum>> GetAlbumsAsync();
        Task<ImageAlbum> CreateAlbumAsync(string name);
        Task AddToAlbumAsync(string albumGuid, string imageGuid);
        Task<List<ImageRecord>> GetImagesByTitleAndSourceAsync(string title, string source);
        Task<List<string>> GetAlbumsForImageAsync(string imageGuid);
    }
}
