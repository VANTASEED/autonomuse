using Autonomuse.Domain.Entities;

namespace Autonomuse.Shared.Contracts
{
    public interface IAudioEnrichmentService
    {
        /// <summary>
        /// Enriches a single audio record with metadata from AcoustID and MusicBrainz.
        /// </summary>
        Task<(bool Success, string Message)> EnrichAudioAsync(AudioRecord record);

        /// <summary>
        /// Enriches all audio records in the library.
        /// </summary>
        Task EnrichAllAudioAsync(Action<string, double>? progressCallback = null);

        /// <summary>
        /// Restores audio metadata to its original state from backup.
        /// </summary>
        Task<(bool Success, string Message)> RestoreAudioAsync(AudioRecord record);
    }
}
