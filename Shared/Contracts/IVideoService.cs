using Autonomuse.Domain.Entities;

namespace Autonomuse.Shared.Contracts
{
    public interface IVideoService
    {
        Task<VideoRecord> AddVideoAsync(string sourceFilePath, string source);
        Task<List<VideoRecord>> GetAllVideosAsync();
        Task<List<MediaPlaylist>> GetPlaylistsAsync();
        Task<MediaPlaylist> CreatePlaylistAsync(string name, string? description = null);
        Task AddToPlaylistAsync(string playlistGuid, string videoGuid);
        Task RemoveFromPlaylistAsync(string playlistGuid, string videoGuid);
        Task AddVideoRecordAsync(VideoRecord record);
        Task<VideoRecord?> GetVideoByTitleAndSourceAsync(string title, string source);
        Task<VideoRecord?> GetVideoByYoutubeIDAsync(string youtubeId);
        Task<List<string>> GetPlaylistsForVideoAsync(string videoGuid);
        Task UpdatePlaylistDescriptionAsync(string guid, string description);
        Task UpdateVideoRecordAsync(VideoRecord record);
        Task UpdatePhysicalTagsAsync(VideoRecord record);
        Task DeleteVideoAsync(string guid);
        Task<string> SaveThumbnailAsync(string guid, System.IO.Stream imageStream);
        Task<List<VideoRecord>> GetVideoInPlaylistAsync(string playlistGuid);
        Task<HashSet<string>> GetVideoGuidsInAnyPlaylistAsync();
        Task CreateVideoBackupAsync(VideoBackup backup);
        Task<VideoBackup?> GetVideoBackupAsync(string guid);
        Task DeleteVideoBackupAsync(string guid);
    }
}
