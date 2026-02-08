using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using SearchOrchestrator.Configuration;
using SearchOrchestrator.Contracts;
using SearchOrchestrator.Models;
using SearchOrchestrator.Services;

namespace SearchOrchestrator.Tests;

public class IndexingOrchestratorTests
{
    private readonly IIndexingSourceRepository _sourceRepo;
    private readonly IIndexingTaskRepository _taskRepo;
    private readonly ISearchEngineClient _searchEngine;
    private readonly IndexingTaskQueue _queue;
    private readonly IndexingOrchestrator _orchestrator;

    public IndexingOrchestratorTests()
    {
        _sourceRepo = new InMemoryIndexingSourceRepository();
        _taskRepo = new InMemoryIndexingTaskRepository();
        _searchEngine = Substitute.For<ISearchEngineClient>();
        var queueLogger = Substitute.For<ILogger<IndexingTaskQueue>>();
        _queue = new IndexingTaskQueue(100, queueLogger);
        var logger = Substitute.For<ILogger<IndexingOrchestrator>>();
        var options = Options.Create(new SearchEngineOptions
        {
            EnqueueTimeoutMs = 5000,
            MaxPollingDurationMinutes = 30
        });

        _orchestrator = new IndexingOrchestrator(
            _sourceRepo, _taskRepo, _searchEngine, _queue, logger, options);
    }

    [Fact]
    public async Task CreateTask_CreatesNewSourceAndTask()
    {
        var request = new CreateIndexingTaskRequest
        {
            SourceUri = "s3://bucket/data",
            SourceName = "Test Source"
        };

        var (task, source, isExisting) = await _orchestrator.CreateTaskAsync(
            request, "corr-1", CancellationToken.None);

        Assert.False(isExisting);
        Assert.NotEqual(Guid.Empty, task.Id);
        Assert.Equal(IndexingTaskStatus.Pending, task.Status);
        Assert.Equal("corr-1", task.CorrelationId);
        Assert.Equal("s3://bucket/data", source.Uri);
        Assert.Equal("Test Source", source.Name);
    }

    [Fact]
    public async Task CreateTask_IdempotentForSameSourceUri()
    {
        var request = new CreateIndexingTaskRequest
        {
            SourceUri = "s3://bucket/data",
            SourceName = "Test Source"
        };

        var (firstTask, _, _) = await _orchestrator.CreateTaskAsync(
            request, "corr-1", CancellationToken.None);

        var (secondTask, _, isExisting) = await _orchestrator.CreateTaskAsync(
            request, "corr-2", CancellationToken.None);

        Assert.True(isExisting);
        Assert.Equal(firstTask.Id, secondTask.Id);
    }

    [Fact]
    public async Task CreateTask_AllowsNewTaskAfterPreviousCompleted()
    {
        var request = new CreateIndexingTaskRequest
        {
            SourceUri = "s3://bucket/data",
            SourceName = "Test Source"
        };

        var (firstTask, _, _) = await _orchestrator.CreateTaskAsync(
            request, "corr-1", CancellationToken.None);

        // Simulate completion
        firstTask.Status = IndexingTaskStatus.Completed;
        _taskRepo.Update(firstTask);

        var (secondTask, _, isExisting) = await _orchestrator.CreateTaskAsync(
            request, "corr-2", CancellationToken.None);

        Assert.False(isExisting);
        Assert.NotEqual(firstTask.Id, secondTask.Id);
    }

    [Fact]
    public async Task CreateTask_AllowsNewTaskAfterPreviousFailed()
    {
        var request = new CreateIndexingTaskRequest
        {
            SourceUri = "s3://bucket/data",
            SourceName = "Test Source"
        };

        var (firstTask, _, _) = await _orchestrator.CreateTaskAsync(
            request, "corr-1", CancellationToken.None);

        firstTask.Status = IndexingTaskStatus.Failed;
        _taskRepo.Update(firstTask);

        var (secondTask, _, isExisting) = await _orchestrator.CreateTaskAsync(
            request, "corr-2", CancellationToken.None);

        Assert.False(isExisting);
        Assert.NotEqual(firstTask.Id, secondTask.Id);
    }

    [Fact]
    public async Task CreateTask_ReusesExistingSource()
    {
        var request1 = new CreateIndexingTaskRequest
        {
            SourceUri = "s3://bucket/data",
            SourceName = "Source V1"
        };
        var (task1, source1, _) = await _orchestrator.CreateTaskAsync(
            request1, "corr-1", CancellationToken.None);

        // Complete the first task so a new one can be created
        task1.Status = IndexingTaskStatus.Completed;
        _taskRepo.Update(task1);

        var request2 = new CreateIndexingTaskRequest
        {
            SourceUri = "s3://bucket/data",
            SourceName = "Source V2"
        };
        var (_, source2, _) = await _orchestrator.CreateTaskAsync(
            request2, "corr-2", CancellationToken.None);

        Assert.Equal(source1.Id, source2.Id);
    }

    [Fact]
    public void GetTask_ReturnsNullForUnknownId()
    {
        var (task, source) = _orchestrator.GetTask(Guid.NewGuid());
        Assert.Null(task);
        Assert.Null(source);
    }

    [Fact]
    public async Task GetTask_ReturnsTaskWithSource()
    {
        var request = new CreateIndexingTaskRequest
        {
            SourceUri = "s3://bucket/data",
            SourceName = "Test Source"
        };
        var (created, _, _) = await _orchestrator.CreateTaskAsync(
            request, "corr-1", CancellationToken.None);

        var (task, source) = _orchestrator.GetTask(created.Id);

        Assert.NotNull(task);
        Assert.NotNull(source);
        Assert.Equal(created.Id, task.Id);
        Assert.Equal("s3://bucket/data", source.Uri);
    }

    [Fact]
    public async Task CancelTask_CancelsPendingTask()
    {
        var request = new CreateIndexingTaskRequest
        {
            SourceUri = "s3://bucket/data",
            SourceName = "Test Source"
        };
        var (created, _, _) = await _orchestrator.CreateTaskAsync(
            request, "corr-1", CancellationToken.None);

        var (cancelled, _) = await _orchestrator.CancelTaskAsync(created.Id, CancellationToken.None);

        Assert.NotNull(cancelled);
        Assert.Equal(IndexingTaskStatus.Cancelled, cancelled.Status);
        Assert.NotNull(cancelled.CompletedAt);
    }

    [Fact]
    public async Task CancelTask_CancelsInProgressTaskAndNotifiesExternalService()
    {
        var request = new CreateIndexingTaskRequest
        {
            SourceUri = "s3://bucket/data",
            SourceName = "Test Source"
        };
        var (created, _, _) = await _orchestrator.CreateTaskAsync(
            request, "corr-1", CancellationToken.None);

        // Simulate in-progress with external task
        created.Status = IndexingTaskStatus.InProgress;
        created.ExternalTaskId = "ext-123";
        _taskRepo.Update(created);

        _searchEngine.CancelIndexingAsync("ext-123", Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var (cancelled, _) = await _orchestrator.CancelTaskAsync(created.Id, CancellationToken.None);

        Assert.NotNull(cancelled);
        Assert.Equal(IndexingTaskStatus.Cancelled, cancelled.Status);
        await _searchEngine.Received(1).CancelIndexingAsync("ext-123", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancelTask_DoesNotCancelCompletedTask()
    {
        var request = new CreateIndexingTaskRequest
        {
            SourceUri = "s3://bucket/data",
            SourceName = "Test Source"
        };
        var (created, _, _) = await _orchestrator.CreateTaskAsync(
            request, "corr-1", CancellationToken.None);

        created.Status = IndexingTaskStatus.Completed;
        _taskRepo.Update(created);

        var (result, _) = await _orchestrator.CancelTaskAsync(created.Id, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(IndexingTaskStatus.Completed, result.Status);
    }

    [Fact]
    public async Task CancelTask_ReturnsNullForUnknownId()
    {
        var result = await _orchestrator.CancelTaskAsync(Guid.NewGuid(), CancellationToken.None);
        Assert.Null(result.Item1);
        Assert.Null(result.Item2);
    }

    [Fact]
    public async Task Search_DelegatesToExternalService()
    {
        _searchEngine.SearchAsync("test query", 10, Arg.Any<CancellationToken>())
            .Returns(Result<ExternalSearchResult>.Success(
                new ExternalSearchResult
                {
                    TotalCount = 1,
                    Items =
                    [
                        new ExternalSearchResultItem
                        {
                            DocumentId = "doc-1",
                            Title = "Test Doc",
                            Snippet = "...test...",
                            Score = 0.95,
                            SourceUri = "s3://bucket/data"
                        }
                    ]
                }));

        var result = await _orchestrator.SearchAsync(
            new SearchRequest { Query = "test query", MaxResults = 10 },
            "test-correlation-id",
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("test query", result.Value!.Query);
        Assert.Equal(1, result.Value.TotalResults);
        Assert.Single(result.Value.Results);
        Assert.Equal("doc-1", result.Value.Results[0].DocumentId);
    }
}
