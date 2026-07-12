namespace Autonomuse.Domain.Entities
{
    public class AudioRecord
    {
        public string GUID { get; set; } = System.Guid.NewGuid().ToString();
        public string FileName { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Extension { get; set; } = string.Empty;
        public string? AlternativeTitle { get; set; }
        public string Source { get; set; } = string.Empty; // "manual upload", "youtube"
        public string? YoutubeID { get; set; }
        public string FilePath { get; set; } = string.Empty;

        // Metadata
        public string? Artist { get; set; }
        public string? Album { get; set; }
        public string? Genre { get; set; }
        public double? Duration { get; set; }
        public int? Bitrate { get; set; }
        public int? SampleRate { get; set; }
        public int? Channels { get; set; }
        public int? Year { get; set; }
        public long? FileSize { get; set; }
        public string? CoverArtPath { get; set; }
        public string? Fingerprint { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public int EnrichmentStatus { get; set; } = 0; // 0: None, 1: MusicBrainz, 2: AppleMusic
    }
}
