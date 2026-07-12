namespace Autonomuse.Domain.Entities
{
    public class EBookChapterPage
    {
        public string GUID { get; set; } = System.Guid.NewGuid().ToString();
        public string ChapterGUID { get; set; } = string.Empty;
        public string EBookGUID { get; set; } = string.Empty;
        public int PageIndex { get; set; }
        public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    }
}
