using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using SearchOrchestrator.Configuration;
using SearchOrchestrator.Models;
using SearchOrchestrator.Services;

namespace SearchOrchestrator.Tests;

public class IndexingBackgroundServiceTests
{
    private readonly InMemoryIndexingSourceRepository _sourceRepo;
    private readonly InMemoryIndexingTaskRepository _taskRepo;
    private readonly ISearchEngineClient _searchEngine;
    private readonly IndexingTaskQueue _queue;
    private readonly IndexingBackgroundService _service;

    public IndexingBackgroundServiceTests()
    {
        _sourceRepo = new InMemoryIndexingSourceRepository();
        _taskRepo = new InMemoryIndexingTaskRepository();
        _searchEngine = Substitute.For<ISearchEngineClient>();
        var queueLogger = Substitute.For<ILogger<IndexingTaskQueue>>();
        _queue = new IndexingTaskQueue(100, queueLogger);
        var logger = Substitute.For<ILogger<IndexingBackgroundService>>();
        var options = Options.Create(new SearchEngineOptions
        {
            PollingIntervalMs = 10,
            MaxPollingDurationMinutes = 30
        });

        _service = new IndexingBackgroundService(
            _queue, _taskRepo, _sourceRepo, _searchEngine, logger, options);
    }

    private (IndexingTask task, IndexingSource source) CreateTestTaskAndSource(string uri = "s3://test")
    {
        var source = new IndexingSource
        {
            Id = Guid.NewGuid(),
            Uri = uri,
            Name = "Test",
            CreatedAt = DateTime.UtcNow
        };
        _sourceRepo.Add(source);

        var task = new IndexingTask
        {
            Id = Guid.NewGuid(),
            SourceId = source.Id,
            Status = IndexingTaskStatus.Pending,
            CorrelationId = "test-corr",
            CreatedAt = DateTime.UtcNow
        };
        _taskRepo.Add(task);

        return (task, source);
    }

    [Fact]
    public async Task ProcessesTask_SuccessfulIndexing()
    {
        var (task, source) = CreateTestTaskAndSource();

        _searchEngine.StartIndexingAsync(source.Uri, Arg.Any<CancellationToken>())
            .Returns(Result<ExternalIndexingResult>.Success(
                new ExternalIndexingResult { ExternalTaskId = "ext-1", Accepted = true }));

        _searchEngine.CheckIndexingStatusAsync("ext-1", Arg.Any<CancellationToken>())
            .Returns(Result<ExternalIndexingStatus>.Success(
                new ExternalIndexingStatus { IsCompleted = true, IsSuccessful = true, Progress = 1.0 }));

        await _queue.TryEnqueueAsync(task.Id, TimeSpan.FromSeconds(10));

        using var cts = new CancellationTokenSource();
        var serviceTask = _service.StartAsync(cts.Token);

        await Task.Delay(500);
        cts.Cancel();
        await _service.StopAsync(CancellationToken.None);

        var updatedTask = _taskRepo.GetById(task.Id);
        Assert.NotNull(updatedTask);
        Assert.Equal(IndexingTaskStatus.Completed, updatedTask.Status);
        Assert.NotNull(updatedTask.CompletedAt);
        Assert.Equal("ext-1", updatedTask.ExternalTaskId);
    }

    [Fact]
    public async Task ProcessesTask_ExternalServiceRejects_TaskFails()
    {
        var (task, source) = CreateTestTaskAndSource();

        _searchEngine.StartIndexingAsync(source.Uri, Arg.Any<CancellationToken>())
            .Returns(Result<ExternalIndexingResult>.Success(
                new ExternalIndexingResult
                {
                    ExternalTaskId = string.Empty,
                    Accepted = false,
                    ErrorMessage = "Service unavailable"
                }));

        await _queue.TryEnqueueAsync(task.Id, TimeSpan.FromSeconds(10));

        using var cts = new CancellationTokenSource();
        var serviceTask = _service.StartAsync(cts.Token);

        await Task.Delay(500);
        cts.Cancel();
        await _service.StopAsync(CancellationToken.None);

        var updatedTask = _taskRepo.GetById(task.Id);
        Assert.NotNull(updatedTask);
        Assert.Equal(IndexingTaskStatus.Failed, updatedTask.Status);
        Assert.Contains("Service unavailable", updatedTask.ErrorMessage);
    }

    [Fact]
    public async Task ProcessesTask_ExternalServiceReturnsFailure_TaskFails()
    {
        var (task, source) = CreateTestTaskAndSource();

        _searchEngine.StartIndexingAsync(source.Uri, Arg.Any<CancellationToken>())
            .Returns(Result<ExternalIndexingResult>.Failure(
                "Connection refused", System.Net.HttpStatusCode.ServiceUnavailable));

        await _queue.TryEnqueueAsync(task.Id, TimeSpan.FromSeconds(10));

        using var cts = new CancellationTokenSource();
        var serviceTask = _service.StartAsync(cts.Token);

        await Task.Delay(500);
        cts.Cancel();
        await _service.StopAsync(CancellationToken.None);

        var updatedTask = _taskRepo.GetById(task.Id);
        Assert.NotNull(updatedTask);
        Assert.Equal(IndexingTaskStatus.Failed, updatedTask.Status);
        Assert.Contains("Connection refused", updatedTask.ErrorMessage);
    }

    [Fact]
    public async Task ProcessesTask_IndexingFailsMidway()
    {
        var (task, source) = CreateTestTaskAndSource();

        _searchEngine.StartIndexingAsync(source.Uri, Arg.Any<CancellationToken>())
            .Returns(Result<ExternalIndexingResult>.Success(
                new ExternalIndexingResult { ExternalTaskId = "ext-1", Accepted = true }));

        _searchEngine.CheckIndexingStatusAsync("ext-1", Arg.Any<CancellationToken>())
            .Returns(Result<ExternalIndexingStatus>.Success(
                new ExternalIndexingStatus
                {
                    IsCompleted = true,
                    IsSuccessful = false,
                    ErrorMessage = "Disk full"
                }));

        await _queue.TryEnqueueAsync(task.Id, TimeSpan.FromSeconds(10));

        using var cts = new CancellationTokenSource();
        var serviceTask = _service.StartAsync(cts.Token);

        await Task.Delay(500);
        cts.Cancel();
        await _service.StopAsync(CancellationToken.None);

        var updatedTask = _taskRepo.GetById(task.Id);
        Assert.NotNull(updatedTask);
        Assert.Equal(IndexingTaskStatus.Failed, updatedTask.Status);
        Assert.Equal("Disk full", updatedTask.ErrorMessage);
    }

    [Fact]
    public async Task ProcessesTask_SkipsCancelledTask()
    {
        var (task, _) = CreateTestTaskAndSource();
        task.Status = IndexingTaskStatus.Cancelled;
        _taskRepo.Update(task);

        await _queue.TryEnqueueAsync(task.Id, TimeSpan.FromSeconds(10));

        using var cts = new CancellationTokenSource();
        var serviceTask = _service.StartAsync(cts.Token);

        await Task.Delay(300);
        cts.Cancel();
        await _service.StopAsync(CancellationToken.None);

        await _searchEngine.DidNotReceive()
            .StartIndexingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessesTask_PollingProgressBeforeCompletion()
    {
        var (task, source) = CreateTestTaskAndSource();

        _searchEngine.StartIndexingAsync(source.Uri, Arg.Any<CancellationToken>())
            .Returns(Result<ExternalIndexingResult>.Success(
                new ExternalIndexingResult { ExternalTaskId = "ext-1", Accepted = true }));

        var callCount = 0;
        _searchEngine.CheckIndexingStatusAsync("ext-1", Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callCount++;
                if (callCount < 3)
                    return Result<ExternalIndexingStatus>.Success(
                        new ExternalIndexingStatus { IsCompleted = false, Progress = callCount * 0.33 });
                return Result<ExternalIndexingStatus>.Success(
                    new ExternalIndexingStatus { IsCompleted = true, IsSuccessful = true, Progress = 1.0 });
            });

        await _queue.TryEnqueueAsync(task.Id, TimeSpan.FromSeconds(10));

        using var cts = new CancellationTokenSource();
        var serviceTask = _service.StartAsync(cts.Token);

        await Task.Delay(500);
        cts.Cancel();
        await _service.StopAsync(CancellationToken.None);

        var updatedTask = _taskRepo.GetById(task.Id);
        Assert.NotNull(updatedTask);
        Assert.Equal(IndexingTaskStatus.Completed, updatedTask.Status);
        Assert.True(callCount >= 3);
    }
}
