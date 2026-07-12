namespace Autonomuse.Domain.Entities
{
    public class ImageAlbumItem
    {
        public string GUID { get; set; } = System.Guid.NewGuid().ToString();
        public string AlbumGUID { get; set; } = string.Empty;
        public string ImageGUID { get; set; } = string.Empty;
        public int OrderIndex { get; set; }
        public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    }
}
