using System.ComponentModel.DataAnnotations;

namespace SearchOrchestrator.Contracts;

public class SearchRequest
{
    [Required]
    [MinLength(1, ErrorMessage = "Query cannot be empty")]
    [MaxLength(1000, ErrorMessage = "Query cannot exceed 1000 characters")]
    public required string Query { get; set; }

    [Range(1, 1000, ErrorMessage = "MaxResults must be between 1 and 1000")]
    public int MaxResults { get; set; } = 20;
}
