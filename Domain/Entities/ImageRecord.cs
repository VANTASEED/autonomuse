namespace Autonomuse.Domain.Entities
{
    public class ImageRecord
    {
        public string GUID { get; set; } = System.Guid.NewGuid().ToString();
        public string FileName { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Extension { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;

        // Metadata
        public int? Width { get; set; }
        public int? Height { get; set; }
        public long? FileSize { get; set; }
        public ulong? PerceptualHash { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
