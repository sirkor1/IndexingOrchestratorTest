namespace SearchOrchestrator.Configuration;

public class SearchEngineOptions
{
    public const string SectionName = "SearchEngine";

    /// <summary>
    /// Base URL of the external search engine HTTP API.
    /// </summary>
    public string BaseUrl { get; set; } = "http://localhost:9200";

    /// <summary>
    /// Interval between status polling calls in milliseconds.
    /// </summary>
    public int PollingIntervalMs { get; set; } = 2000;

    /// <summary>
    /// Maximum capacity of the task queue.
    /// </summary>
    public int QueueCapacity { get; set; } = 100;

    /// <summary>
    /// Timeout for enqueuing tasks when queue is full (milliseconds).
    /// </summary>
    public int EnqueueTimeoutMs { get; set; } = 5000;

    /// <summary>
    /// Maximum duration for polling indexing status before timeout (minutes).
    /// </summary>
    public int MaxPollingDurationMinutes { get; set; } = 30;

    /// <summary>
    /// Maximum number of indexing tasks processed concurrently by the background service.
    /// </summary>
    public int MaxConcurrency { get; set; } = 5;
}
