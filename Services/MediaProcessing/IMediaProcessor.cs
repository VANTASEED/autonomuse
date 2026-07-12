namespace Autonomuse.Services.MediaProcessing
{
    /// <summary>
    /// Interface for media processing pipeline steps.
    /// Per architecture spec: Media processing is a dedicated service layer concern.
    /// 
    /// Implementations handle specific media transformations such as:
    /// - Audio extraction from video
    /// - Image compression/resizing
    /// - Thumbnail generation
    /// - Metadata extraction
    /// </summary>
    public interface IMediaProcessor
    {
        /// <summary>
        /// Gets the media types this processor supports.
        /// </summary>
        IReadOnlyCollection<string> SupportedExtensions { get; }

        /// <summary>
        /// Processes a media file and returns the path to the processed output.
        /// </summary>
        /// <param name="inputPath">Path to the source file.</param>
        /// <param name="outputDirectory">Directory to write the processed file.</param>
        /// <param name="onProgress">Optional progress callback (0-100).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Path to the processed output file.</returns>
        Task<string> ProcessAsync(
            string inputPath,
            string outputDirectory,
            Action<int>? onProgress = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks whether the processor can handle the given file.
        /// </summary>
        bool CanProcess(string filePath);
    }
}
