using System.Threading.Tasks.Dataflow;
using SearchOrchestrator.Configuration;
using SearchOrchestrator.Models;
using Microsoft.Extensions.Options;

namespace SearchOrchestrator.Services;

/// <summary>
/// Background service that processes indexing tasks from the queue.
/// Uses TPL Dataflow ActionBlock for concurrent task processing.
/// HTTP-level retries are handled by Polly at the HttpClient pipeline level.
/// </summary>
public class IndexingBackgroundService : BackgroundService
{
    private readonly IndexingTaskQueue _queue;
    private readonly IIndexingTaskRepository _taskRepo;
    private readonly IIndexingSourceRepository _sourceRepo;
    private readonly ISearchEngineClient _searchEngine;
    private readonly ILogger<IndexingBackgroundService> _logger;
    private readonly SearchEngineOptions _options;

    public IndexingBackgroundService(
        IndexingTaskQueue queue,
        IIndexingTaskRepository taskRepo,
        IIndexingSourceRepository sourceRepo,
        ISearchEngineClient searchEngine,
        ILogger<IndexingBackgroundService> logger,
        IOptions<SearchEngineOptions> options)
    {
        _queue = queue;
        _taskRepo = taskRepo;
        _sourceRepo = sourceRepo;
        _searchEngine = searchEngine;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Indexing background service started with MaxConcurrency={MaxConcurrency}",
            _options.MaxConcurrency);

        var block = new ActionBlock<Guid>(
            taskId => ProcessTaskAsync(taskId, stoppingToken),
            new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = _options.MaxConcurrency,
                CancellationToken = stoppingToken
            });

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var taskId = await _queue.DequeueAsync(stoppingToken);
                await block.SendAsync(taskId, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }

        block.Complete();
        await block.Completion;

        _logger.LogInformation("Indexing background service stopped");
    }

    private async Task ProcessTaskAsync(Guid taskId, CancellationToken ct)
    {
        try
        {
            await ProcessTaskCoreAsync(taskId, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error processing task {TaskId}", taskId);
        }
    }

    private async Task ProcessTaskCoreAsync(Guid taskId, CancellationToken ct)
    {
        var task = _taskRepo.GetById(taskId);
        if (task == null)
        {
            _logger.LogWarning("Task {TaskId} not found, skipping", taskId);
            return;
        }

        if (task.Status == IndexingTaskStatus.Cancelled)
        {
            _logger.LogInformation("Task {TaskId} was cancelled before processing, skipping", taskId);
            return;
        }

        var source = _sourceRepo.GetById(task.SourceId);
        if (source == null)
        {
            _logger.LogError("Source {SourceId} not found for task {TaskId}", task.SourceId, taskId);
            task.MarkAsFailed("Source not found");
            _taskRepo.Update(task);
            return;
        }

        var startResult = await _searchEngine.StartIndexingAsync(source.Uri, ct);

        if (!startResult.IsSuccess)
        {
            task.MarkAsFailed($"Failed to start indexing: {startResult.Error}");
            _taskRepo.Update(task);
            _logger.LogError("Task {TaskId} failed: could not start indexing — {Error}", taskId, startResult.Error);
            return;
        }

        if (!startResult.Value!.Accepted)
        {
            task.MarkAsFailed(startResult.Value.ErrorMessage ?? "Indexing rejected by external service");
            _taskRepo.Update(task);
            _logger.LogWarning("Task {TaskId}: external service rejected indexing request — {ErrorMessage}",
                taskId, task.ErrorMessage);
            return;
        }

        task.MarkAsStarted(startResult.Value.ExternalTaskId);
        _taskRepo.Update(task);

        _logger.LogInformation("Task {TaskId} started, sent to external service with ExternalTaskId {ExternalTaskId}",
            taskId, startResult.Value.ExternalTaskId);

        var pollingStartTime = DateTime.UtcNow;
        var maxPollingDuration = TimeSpan.FromMinutes(_options.MaxPollingDurationMinutes);

        while (!ct.IsCancellationRequested)
        {
            if (DateTime.UtcNow - pollingStartTime > maxPollingDuration)
            {
                task.MarkAsFailed($"Indexing timeout exceeded ({_options.MaxPollingDurationMinutes} minutes)");
                _taskRepo.Update(task);
                _logger.LogError("Task {TaskId} failed: polling timeout exceeded after {Minutes} minutes",
                    taskId, _options.MaxPollingDurationMinutes);
                return;
            }

            task = _taskRepo.GetById(taskId)!;
            if (task.Status == IndexingTaskStatus.Cancelled)
            {
                _logger.LogInformation("Task {TaskId} was cancelled during processing", taskId);
                return;
            }

            var statusResult = await _searchEngine.CheckIndexingStatusAsync(startResult.Value.ExternalTaskId, ct);

            if (!statusResult.IsSuccess)
            {
                _logger.LogWarning("Error polling indexing status for task {TaskId}: {Error}",
                    taskId, statusResult.Error);
                await Task.Delay(_options.PollingIntervalMs, ct);
                continue;
            }

            var status = statusResult.Value!;

            if (status.IsCompleted)
            {
                if (status.IsSuccessful)
                    task.MarkAsCompleted();
                else
                    task.MarkAsFailed(status.ErrorMessage ?? "Unknown error");

                _taskRepo.Update(task);

                _logger.LogInformation("Task {TaskId} completed with status {TaskStatus}, ExternalTaskId {ExternalTaskId}",
                    taskId, task.Status, startResult.Value.ExternalTaskId);
                return;
            }

            _logger.LogDebug("Task {TaskId} indexing in progress, ExternalTaskId {ExternalTaskId}, Progress {Progress}",
                taskId, startResult.Value.ExternalTaskId, status.Progress);

            await Task.Delay(_options.PollingIntervalMs, ct);
        }
    }
}
