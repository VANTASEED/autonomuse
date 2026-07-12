using System;

namespace Autonomuse.Domain.Entities
{
    public class AudioBackup
    {
        public string GUID { get; set; } = string.Empty;
        public string? AlternativeTitle { get; set; }
        public string? YoutubeID { get; set; }
        public string? Artist { get; set; }
        public string? Album { get; set; }
        public string? Genre { get; set; }
        public int? Year { get; set; }
        public string? CoverArtPath { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
