using Autonomuse.Domain.Entities;

namespace Autonomuse.Shared.Contracts
{
    public interface IAudioService
    {
        Task<AudioRecord> AddAudioAsync(string sourceFilePath, string source);
        Task<List<AudioRecord>> GetAllAudioAsync();
        Task<List<MediaPlaylist>> GetPlaylistsAsync();
        Task<MediaPlaylist> CreatePlaylistAsync(string name, string? description = null);
        Task AddToPlaylistAsync(string playlistGuid, string audioGuid);
        Task RemoveFromPlaylistAsync(string playlistGuid, string audioGuid);
        Task AddAudioRecordAsync(AudioRecord record);
        Task<AudioRecord?> GetAudioByTitleAndSourceAsync(string title, string source);
        Task<AudioRecord?> GetAudioByYoutubeIDAsync(string youtubeId);
        Task<List<string>> GetPlaylistsForAudioAsync(string audioGuid);
        Task UpdatePlaylistDescriptionAsync(string guid, string description);
        Task UpdateAudioRecordAsync(AudioRecord record);
        Task UpdatePhysicalTagsAsync(AudioRecord record);
        Task CreateAudioBackupAsync(AudioBackup backup);
        Task<AudioBackup?> GetAudioBackupAsync(string guid);
        Task DeleteAudioBackupAsync(string guid);

        Task<string?> GenerateFingerprintAsync(string filePath);
        Task<List<AudioRecord>> GetAudioInPlaylistAsync(string playlistGuid);
        Task<HashSet<string>> GetAudioGuidsInAnyPlaylistAsync();
        Task DeleteAudioAsync(string guid);
        Task<string> SaveCoverArtAsync(string guid, System.IO.Stream imageStream);
    }
}
