namespace Autonomuse.Shared.Utilities
{
    public static class FilePathSanitizer
    {
        public static string SterilizeTitle(string title, IEnumerable<string>? tags)
        {
            if (string.IsNullOrWhiteSpace(title) || tags == null || !tags.Any())
                return title;

            var result = title;
            foreach (var tag in tags)
            {
                if (string.IsNullOrWhiteSpace(tag)) continue;
                result = System.Text.RegularExpressions.Regex.Replace(result, System.Text.RegularExpressions.Regex.Escape(tag), "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }

            result = System.Text.RegularExpressions.Regex.Replace(result, @"\s+", " ");
            return result.Trim();
        }
    }
}
