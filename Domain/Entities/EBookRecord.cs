namespace Autonomuse.Domain.Entities
{
    public class EBookRecord
    {
        public string GUID { get; set; } = System.Guid.NewGuid().ToString();
        public string FileName { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Extension { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;

        // Metadata
        public string? Author { get; set; }
        public int? PageCount { get; set; }
        public long? FileSize { get; set; }
        public string? CoverPath { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
