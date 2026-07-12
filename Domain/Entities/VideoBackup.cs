namespace Autonomuse.Domain.Entities
{
    public class VideoBackup
    {
        public string GUID { get; set; } = string.Empty;
        public string? AlternativeTitle { get; set; }
        public string? Artist { get; set; }
        public string? Genre { get; set; }
        public int? Year { get; set; }
        public string? ThumbnailPath { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
