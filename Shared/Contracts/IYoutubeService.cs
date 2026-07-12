using System.Collections.Generic;
using System.Threading.Tasks;
using Autonomuse.Domain.Entities;

namespace Autonomuse.Shared.Contracts
{
    public class YoutubeDownloadFailure
    {
        public string Title { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
    }

    public class YoutubeDownloadResult
    {
        public int Success { get; set; }
        public int Duplicate { get; set; }
        public int Mapped { get; set; }
        public int Error { get; set; }
        public List<YoutubeDownloadFailure> Failures { get; set; } = new();
    }

    public interface IYoutubeService
    {
        /// <summary>
        /// Downloads audio from YouTube and adds to library.
        /// </summary>
        Task<YoutubeDownloadResult> DownloadAudioAsync(string url, Action<string>? onProgress = null, Func<Task>? onPlaylistCreated = null, string? manualPlaylistGuid = null);

        /// <summary>
        /// Downloads video from YouTube and adds to library.
        /// </summary>
        Task<YoutubeDownloadResult> DownloadVideoAsync(string url, Action<string>? onProgress = null, Func<Task>? onPlaylistCreated = null, string? manualPlaylistGuid = null);
    }
}
