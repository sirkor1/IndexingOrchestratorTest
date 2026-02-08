namespace SearchOrchestrator.Models;

/// <summary>
/// Represents an indexing job managed by the orchestrator.
/// Tracks the lifecycle of an indexing request sent to the external search engine.
/// </summary>
public class IndexingTask
{
    public Guid Id { get; set; }
    public Guid SourceId { get; set; }
    public IndexingTaskStatus Status { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Task ID returned by the external search engine.
    /// </summary>
    public string? ExternalTaskId { get; set; }

    public string CorrelationId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Marks the task as started with external task ID.
    /// </summary>
    public void MarkAsStarted(string externalTaskId)
    {
        if (Status != IndexingTaskStatus.Pending)
            throw new InvalidOperationException(
                $"Task can only be started from Pending status. Current status: {Status}");

        Status = IndexingTaskStatus.InProgress;
        ExternalTaskId = externalTaskId;
        StartedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Marks the task as successfully completed.
    /// </summary>
    public void MarkAsCompleted()
    {
        if (Status != IndexingTaskStatus.InProgress)
            throw new InvalidOperationException(
                $"Task can only be completed from InProgress status. Current status: {Status}");

        Status = IndexingTaskStatus.Completed;
        CompletedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Marks the task as failed with error message.
    /// Allowed from Pending or InProgress status only.
    /// </summary>
    public void MarkAsFailed(string errorMessage)
    {
        if (Status is IndexingTaskStatus.Completed or IndexingTaskStatus.Failed or IndexingTaskStatus.Cancelled)
            throw new InvalidOperationException(
                $"Cannot fail task in terminal status: {Status}");

        Status = IndexingTaskStatus.Failed;
        ErrorMessage = errorMessage;
        CompletedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Marks the task as cancelled.
    /// </summary>
    public void MarkAsCancelled()
    {
        if (Status is IndexingTaskStatus.Completed or IndexingTaskStatus.Failed)
            throw new InvalidOperationException(
                $"Cannot cancel task in terminal status: {Status}");

        Status = IndexingTaskStatus.Cancelled;
        CompletedAt = DateTime.UtcNow;
    }
}
