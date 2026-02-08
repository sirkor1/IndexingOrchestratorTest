namespace SearchOrchestrator.Configuration;

/// <summary>
/// Named HTTP clients for IHttpClientFactory.
/// </summary>
public static class HttpClientNames
{
    public const string SearchEngine = "SearchEngine";
}

/// <summary>
/// Policy endpoint names for retry configuration.
/// </summary>
public static class PolicyEndpoints
{
    public const string StartIndexing = "StartIndexing";
    public const string CheckStatus = "CheckStatus";
    public const string CancelIndexing = "CancelIndexing";
    public const string Search = "Search";
}
