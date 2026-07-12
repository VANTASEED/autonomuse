namespace Autonomuse.Domain.Entities
{
    public class EBookChapter
    {
        public string GUID { get; set; } = System.Guid.NewGuid().ToString();
        public string SeriesGUID { get; set; } = string.Empty;
        public int ChapterNumber { get; set; }
        public string Title { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
