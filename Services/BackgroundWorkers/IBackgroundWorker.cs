namespace Autonomuse.Services.BackgroundWorkers
{
    /// <summary>
    /// Interface for queue-based background processing workers.
    /// Per architecture spec: "Queue-based processing system (in-memory)" and
    /// "Dedicated background workers (singleton services)".
    /// 
    /// Implementations must:
    /// - Not block the UI thread
    /// - Report progress via ViewModels (observable state)
    /// - Use Task.Run for lightweight jobs
    /// </summary>
    public interface IBackgroundWorker
    {
        /// <summary>
        /// Enqueue a unit of work for background processing.
        /// </summary>
        Task EnqueueAsync(Func<CancellationToken, Task> workItem);

        /// <summary>
        /// Start processing enqueued work items.
        /// </summary>
        Task StartAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Stop the worker gracefully.
        /// </summary>
        Task StopAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Gets the current number of items in the queue.
        /// </summary>
        int QueueCount { get; }

        /// <summary>
        /// Gets whether the worker is currently processing.
        /// </summary>
        bool IsProcessing { get; }
    }
}
