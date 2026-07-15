namespace Autonomuse.Domain.Entities
{
    public class VideoWatchPlaylist
    {
        public string GUID { get; set; } = Guid.NewGuid().ToString();
        public string Url { get; set; } = string.Empty;
        public string? PlaylistName { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastCheckedAt { get; set; }
        public bool IsValid { get; set; } = true;
        public string? LastError { get; set; }
        public string? LastStatus { get; set; }
    }
}
