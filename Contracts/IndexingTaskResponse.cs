namespace SearchOrchestrator.Contracts;

public class IndexingTaskResponse
{
    public Guid TaskId { get; set; }
    public Guid SourceId { get; set; }
    public string SourceUri { get; set; } = string.Empty;
    public string SourceName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
