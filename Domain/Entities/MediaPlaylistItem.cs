namespace Autonomuse.Domain.Entities
{
    public class MediaPlaylistItem
    {
        public string GUID { get; set; } = System.Guid.NewGuid().ToString();
        public string PlaylistGUID { get; set; } = string.Empty;
        public string MediaGUID { get; set; } = string.Empty;
        public int OrderIndex { get; set; }
        public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    }
}
