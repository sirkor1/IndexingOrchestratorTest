using Microsoft.Extensions.Options;
using SearchOrchestrator.Configuration;
using SearchOrchestrator.Contracts;
using SearchOrchestrator.Models;

namespace SearchOrchestrator.Services;

/// <summary>
/// Core orchestration logic. Coordinates task creation, idempotency, cancellation, and search delegation.
/// </summary>
public class IndexingOrchestrator
{
    private readonly IIndexingSourceRepository _sourceRepo;
    private readonly IIndexingTaskRepository _taskRepo;
    private readonly ISearchEngineClient _searchEngine;
    private readonly IndexingTaskQueue _queue;
    private readonly ILogger<IndexingOrchestrator> _logger;
    private readonly SearchEngineOptions _options;

    public IndexingOrchestrator(
        IIndexingSourceRepository sourceRepo,
        IIndexingTaskRepository taskRepo,
        ISearchEngineClient searchEngine,
        IndexingTaskQueue queue,
        ILogger<IndexingOrchestrator> logger,
        IOptions<SearchEngineOptions> options)
    {
        _sourceRepo = sourceRepo;
        _taskRepo = taskRepo;
        _searchEngine = searchEngine;
        _queue = queue;
        _logger = logger;
        _options = options.Value;
    }

    /// <summary>
    /// Creates an indexing task or returns an existing active task for the same source (idempotency).
    /// </summary>
    public async Task<(IndexingTask Task, IndexingSource Source, bool IsExisting)> CreateTaskAsync(
        CreateIndexingTaskRequest request, string correlationId, CancellationToken ct)
    {
        var source = _sourceRepo.GetByUri(request.SourceUri);
        if (source == null)
        {
            source = new IndexingSource
            {
                Id = Guid.NewGuid(),
                Uri = request.SourceUri,
                Name = request.SourceName,
                CreatedAt = DateTime.UtcNow
            };
            _sourceRepo.Add(source);

            _logger.LogInformation("Created new indexing source {SourceId} for {SourceUri}",
                source.Id, source.Uri);
        }

        var existingTask = _taskRepo.GetActiveBySourceId(source.Id);
        if (existingTask != null)
        {
            _logger.LogInformation("Returning existing active task {TaskId} for source {SourceId}",
                existingTask.Id, source.Id);
            return (existingTask, source, true);
        }

        var task = new IndexingTask
        {
            Id = Guid.NewGuid(),
            SourceId = source.Id,
            Status = IndexingTaskStatus.Pending,
            CorrelationId = correlationId,
            CreatedAt = DateTime.UtcNow
        };
        _taskRepo.Add(task);

        _logger.LogInformation("Created indexing task {TaskId} for source {SourceId}, CorrelationId {CorrelationId}",
            task.Id, source.Id, correlationId);

        var enqueueTimeout = TimeSpan.FromMilliseconds(_options.EnqueueTimeoutMs);
        var enqueued = await _queue.TryEnqueueAsync(task.Id, enqueueTimeout, ct);

        if (!enqueued)
        {
            task.MarkAsFailed("Service is overloaded, queue is at capacity");
            _taskRepo.Update(task);

            _logger.LogWarning(
                "Task {TaskId} failed: queue at capacity after {TimeoutMs}ms wait",
                task.Id, _options.EnqueueTimeoutMs);
        }

        return (task, source, false);
    }

    /// <summary>
    /// Returns a task by ID with its associated source.
    /// </summary>
    public (IndexingTask? Task, IndexingSource? Source) GetTask(Guid taskId)
    {
        var task = _taskRepo.GetById(taskId);
        if (task == null)
            return (null, null);

        var source = _sourceRepo.GetById(task.SourceId);
        return (task, source);
    }

    /// <summary>
    /// Attempts to cancel an active task.
    /// </summary>
    public async Task<(IndexingTask?, IndexingSource?)> CancelTaskAsync(Guid taskId, CancellationToken ct)
    {
        var task = _taskRepo.GetById(taskId);
        if (task == null)
            return (null, null);

        var source = _sourceRepo.GetById(task.SourceId);

        if (task.Status is not (IndexingTaskStatus.Pending or IndexingTaskStatus.InProgress))
        {
            _logger.LogWarning("Cannot cancel task {TaskId} in terminal status {TaskStatus}",
                taskId, task.Status);
            return (task, source);
        }

        if (task.Status == IndexingTaskStatus.InProgress && task.ExternalTaskId != null)
        {
            var cancelResult = await _searchEngine.CancelIndexingAsync(task.ExternalTaskId, ct);
            if (!cancelResult.IsSuccess)
            {
                _logger.LogWarning("Failed to cancel external task {ExternalTaskId}: {Error}",
                    task.ExternalTaskId, cancelResult.Error);
            }
        }

        task.MarkAsCancelled();
        _taskRepo.Update(task);

        _logger.LogInformation("Task {TaskId} cancelled", taskId);
        return (task, source);
    }

    /// <summary>
    /// Delegates a search query to the external search engine.
    /// </summary>
    public async Task<Result<SearchResponse>> SearchAsync(SearchRequest request, string correlationId, CancellationToken ct)
    {
        _logger.LogInformation("Executing search query {SearchQuery} with MaxResults {MaxResults}, CorrelationId {CorrelationId}",
            request.Query, request.MaxResults, correlationId);

        var searchResult = await _searchEngine.SearchAsync(request.Query, request.MaxResults, ct);

        if (!searchResult.IsSuccess)
        {
            _logger.LogWarning("Search query failed: {SearchQuery}, Error {Error}, StatusCode {StatusCode}, CorrelationId {CorrelationId}",
                request.Query, searchResult.Error, searchResult.StatusCode, correlationId);
            return Result<SearchResponse>.Failure(searchResult.Error!, searchResult.StatusCode);
        }

        var result = searchResult.Value!;
        return Result<SearchResponse>.Success(new SearchResponse
        {
            Query = request.Query,
            TotalResults = result.TotalCount,
            Results = result.Items.Select(item => new SearchResultItem
            {
                DocumentId = item.DocumentId,
                Title = item.Title,
                Snippet = item.Snippet,
                Score = item.Score,
                SourceUri = item.SourceUri
            }).ToList()
        });
    }
}
