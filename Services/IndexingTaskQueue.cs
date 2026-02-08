using System.Threading.Channels;

namespace SearchOrchestrator.Services;

/// <summary>
/// Bounded channel-based queue for indexing task IDs.
/// Decouples API request handling from background processing.
/// </summary>
public class IndexingTaskQueue
{
    private readonly Channel<Guid> _channel;
    private readonly ILogger<IndexingTaskQueue> _logger;

    public IndexingTaskQueue(int capacity, ILogger<IndexingTaskQueue> logger)
    {
        _channel = Channel.CreateBounded<Guid>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait
        });
        _logger = logger;
    }

    /// <summary>
    /// Tries to enqueue a task with a timeout. Returns false if queue is full after timeout.
    /// </summary>
    public async ValueTask<bool> TryEnqueueAsync(
        Guid taskId,
        TimeSpan timeout,
        CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        try
        {
            await _channel.Writer.WriteAsync(taskId, cts.Token);
            return true;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Failed to enqueue task {TaskId}: queue full after {TimeoutMs}ms",
                taskId, timeout.TotalMilliseconds);
            return false;
        }
    }

    public async ValueTask<Guid> DequeueAsync(CancellationToken ct)
    {
        return await _channel.Reader.ReadAsync(ct);
    }
}
