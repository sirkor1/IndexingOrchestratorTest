using System.ComponentModel.DataAnnotations;

namespace SearchOrchestrator.Contracts;

public class CreateIndexingTaskRequest
{
    /// <summary>
    /// URI of the data source to index (file path, S3 URI, network share, etc.).
    /// Used as idempotency key: if an active task for this URI exists, it will be returned.
    /// </summary>
    [Required]
    [MaxLength(2000, ErrorMessage = "SourceUri cannot exceed 2000 characters")]
    [MinLength(1, ErrorMessage = "SourceUri cannot be empty")]
    public required string SourceUri { get; set; }

    /// <summary>
    /// Human-readable name for the source.
    /// </summary>
    [Required]
    [MaxLength(500, ErrorMessage = "SourceName cannot exceed 500 characters")]
    [MinLength(1, ErrorMessage = "SourceName cannot be empty")]
    public required string SourceName { get; set; }
}
