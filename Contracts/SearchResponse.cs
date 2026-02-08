namespace SearchOrchestrator.Contracts;

public class SearchResponse
{
    public required string Query { get; set; }
    public int TotalResults { get; set; }
    public List<SearchResultItem> Results { get; set; } = [];
}

public class SearchResultItem
{
    public required string DocumentId { get; set; }
    public required string Title { get; set; }
    public required string Snippet { get; set; }
    public double Score { get; set; }
    public required string SourceUri { get; set; }
}
