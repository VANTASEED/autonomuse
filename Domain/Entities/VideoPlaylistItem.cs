namespace Autonomuse.Domain.Entities
{
    public class VideoPlaylistItem
    {
        public string GUID { get; set; } = System.Guid.NewGuid().ToString();
        public string PlaylistGUID { get; set; } = string.Empty;
        public string VideoGUID { get; set; } = string.Empty;
        public int OrderIndex { get; set; }
        public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    }
}
