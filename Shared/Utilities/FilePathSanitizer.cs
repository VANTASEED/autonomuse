namespace Autonomuse.Shared.Utilities
{
    /// <summary>
    /// Static utility for file path validation, sanitization, and safety checks.
    /// Per architecture spec: validate file existence, prevent path traversal, sanitize all file paths.
    /// </summary>
    public static class FilePathSanitizer
    {
        private static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars();
        private static readonly char[] InvalidPathChars = Path.GetInvalidPathChars();

        /// <summary>
        /// Removes invalid characters from a file name.
        /// </summary>
        public static string SanitizeFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return string.Empty;

            var sanitized = string.Join("_", fileName.Split(InvalidFileNameChars, StringSplitOptions.RemoveEmptyEntries));
            return sanitized.Trim();
        }

        /// <summary>
        /// Sanitizes a full path by removing invalid characters and preventing path traversal attacks.
        /// </summary>
        public static string SanitizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            // Block path traversal sequences
            var sanitized = path.Replace("..", string.Empty);

            // Remove invalid path characters
            sanitized = string.Join("", sanitized.Split(InvalidPathChars, StringSplitOptions.RemoveEmptyEntries));

            return sanitized.Trim();
        }

        /// <summary>
        /// Validates that a file exists at the given path. Returns false for null/empty paths.
        /// </summary>
        public static bool ValidateFileExists(string? filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return false;

            return File.Exists(filePath);
        }

        /// <summary>
        /// Ensures a directory exists, creating it if necessary. Returns the directory path.
        /// </summary>
        public static string EnsureDirectoryExists(string directoryPath)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
                throw new ArgumentException("Directory path cannot be null or empty.", nameof(directoryPath));

            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            return directoryPath;
        }

        /// <summary>
        /// Validates that a target path is within an allowed root directory (prevents path traversal).
        /// </summary>
        public static bool IsPathWithinRoot(string targetPath, string rootPath)
        {
            if (string.IsNullOrWhiteSpace(targetPath) || string.IsNullOrWhiteSpace(rootPath))
                return false;

            var fullTarget = Path.GetFullPath(targetPath);
            var fullRoot = Path.GetFullPath(rootPath);

            return fullTarget.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Removes sterilized tags from a title string.
        /// Visual only, does not affect the source data.
        /// </summary>
        public static string SterilizeTitle(string title, IEnumerable<string>? tags)
        {
            if (string.IsNullOrWhiteSpace(title) || tags == null || !tags.Any())
                return title;

            var result = title;
            foreach (var tag in tags)
            {
                if (string.IsNullOrWhiteSpace(tag)) continue;
                // Case-insensitive removal of the tag
                result = System.Text.RegularExpressions.Regex.Replace(result, System.Text.RegularExpressions.Regex.Escape(tag), "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }

            // Clean up double spaces or trailing/leading spaces after removal
            result = System.Text.RegularExpressions.Regex.Replace(result, @"\s+", " ");
            return result.Trim();
        }
    }
}
