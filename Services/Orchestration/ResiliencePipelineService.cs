using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace Autonomuse.Services.Orchestration
{
    /// <summary>
    /// Provides pre-configured Polly resilience pipelines for the application.
    /// Per architecture spec: "Retry policies (Polly)", "Retry with exponential backoff",
    /// "Graceful failure handling", "Timeout management".
    /// </summary>
    public class ResiliencePipelineService
    {
        private readonly ILogger<ResiliencePipelineService> _logger;
        private readonly ResiliencePipeline _defaultPipeline;
        private readonly ResiliencePipeline _externalApiPipeline;

        public ResiliencePipelineService(ILogger<ResiliencePipelineService> logger)
        {
            _logger = logger;

            _defaultPipeline = new ResiliencePipelineBuilder()
                .AddRetry(new RetryStrategyOptions
                {
                    MaxRetryAttempts = 3,
                    Delay = TimeSpan.FromSeconds(1),
                    BackoffType = DelayBackoffType.Exponential,
                    OnRetry = args =>
                    {
                        _logger.LogWarning(
                            "Retry attempt {Attempt} after {Delay}ms due to: {Exception}",
                            args.AttemptNumber,
                            args.RetryDelay.TotalMilliseconds,
                            args.Outcome.Exception?.Message ?? "Unknown");
                        return ValueTask.CompletedTask;
                    }
                })
                .AddTimeout(TimeSpan.FromSeconds(30))
                .Build();

            _externalApiPipeline = new ResiliencePipelineBuilder()
                .AddRetry(new RetryStrategyOptions
                {
                    MaxRetryAttempts = 5,
                    Delay = TimeSpan.FromSeconds(2),
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    OnRetry = args =>
                    {
                        _logger.LogWarning(
                            "External API retry attempt {Attempt} after {Delay}ms due to: {Exception}",
                            args.AttemptNumber,
                            args.RetryDelay.TotalMilliseconds,
                            args.Outcome.Exception?.Message ?? "Unknown");
                        return ValueTask.CompletedTask;
                    }
                })
                .AddTimeout(TimeSpan.FromSeconds(60))
                .Build();
        }

        /// <summary>
        /// Gets the default resilience pipeline (3 retries, exponential backoff, 30s timeout).
        /// Suitable for local I/O operations and database calls.
        /// </summary>
        public ResiliencePipeline Default => _defaultPipeline;

        /// <summary>
        /// Gets the external API resilience pipeline (5 retries, jittered exponential backoff, 60s timeout).
        /// Suitable for network calls to YouTube, metadata APIs, etc.
        /// </summary>
        public ResiliencePipeline ExternalApi => _externalApiPipeline;
    }
}
