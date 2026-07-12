namespace Autonomuse.Domain.Entities
{
    public class VideoRecord
    {
        public string GUID { get; set; } = System.Guid.NewGuid().ToString();
        public string FileName { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Extension { get; set; } = string.Empty;
        public string? AlternativeTitle { get; set; }
        public string Source { get; set; } = string.Empty;
        public string? YoutubeID { get; set; }
        public string FilePath { get; set; } = string.Empty;
        public string? Genre { get; set; }
        public string? Artist { get; set; }

        // Metadata
        public double? Duration { get; set; }
        public string? Resolution { get; set; }
        public int? Bitrate { get; set; }
        public int? SampleRate { get; set; }
        public int? Channels { get; set; }
        public int? Year { get; set; }
        public long? FileSize { get; set; }
        public string? ThumbnailPath { get; set; }
        public int MetadataStatus { get; set; } = 0; // 0: Standard, 1: Modified, >=10: Locked (encoded)
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
