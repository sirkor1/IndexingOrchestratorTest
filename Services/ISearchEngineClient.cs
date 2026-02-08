using SearchOrchestrator.Models;

namespace SearchOrchestrator.Services;

/// <summary>
/// Contract for communication with the external search engine service.
/// The external service handles actual file reading, indexing, and search.
/// </summary>
public interface ISearchEngineClient
{
    /// <summary>
    /// Sends an indexing request to the external search engine.
    /// Returns an external task identifier for tracking.
    /// </summary>
    Task<Result<ExternalIndexingResult>> StartIndexingAsync(string sourceUri, CancellationToken ct);

    /// <summary>
    /// Polls the external search engine for the status of an indexing task.
    /// </summary>
    Task<Result<ExternalIndexingStatus>> CheckIndexingStatusAsync(string externalTaskId, CancellationToken ct);

    /// <summary>
    /// Requests the external service to cancel an indexing task.
    /// </summary>
    Task<Result> CancelIndexingAsync(string externalTaskId, CancellationToken ct);

    /// <summary>
    /// Performs a search query against the external search engine's index.
    /// </summary>
    Task<Result<ExternalSearchResult>> SearchAsync(string query, int maxResults, CancellationToken ct);
}

public class ExternalIndexingResult
{
    public required string ExternalTaskId { get; set; }
    public bool Accepted { get; set; }
    public string? ErrorMessage { get; set; }
}

public class ExternalIndexingStatus
{
    public bool IsCompleted { get; set; }
    public bool IsSuccessful { get; set; }
    public string? ErrorMessage { get; set; }
    public double Progress { get; set; }
}

public class ExternalSearchResult
{
    public List<ExternalSearchResultItem> Items { get; set; } = [];
    public int TotalCount { get; set; }
}

public class ExternalSearchResultItem
{
    public required string DocumentId { get; set; }
    public required string Title { get; set; }
    public required string Snippet { get; set; }
    public double Score { get; set; }
    public required string SourceUri { get; set; }
}
