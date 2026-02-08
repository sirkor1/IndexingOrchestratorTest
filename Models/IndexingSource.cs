namespace SearchOrchestrator.Models;

/// <summary>
/// Represents a data source that can be indexed by the external search engine.
/// </summary>
public class IndexingSource
{
    public Guid Id { get; set; }
    public required string Uri { get; set; }
    public required string Name { get; set; }
    public DateTime CreatedAt { get; set; }
}
