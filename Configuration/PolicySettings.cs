namespace SearchOrchestrator.Configuration;

public class PolicySettings
{
    public const string SectionName = "SearchEngine:Policies";

    public PollyRetrySettings Default { get; set; } = new();

    /// <summary>
    /// Per-endpoint overrides. Key = endpoint name (e.g. "StartIndexing", "CheckStatus").
    /// Falls back to <see cref="Default"/> when endpoint is not found.
    /// </summary>
    public Dictionary<string, PollyRetrySettings> Endpoints { get; set; } = new();

    /// <summary>
    /// Circuit breaker configuration.
    /// </summary>
    public CircuitBreakerSettings CircuitBreaker { get; set; } = new();

    public PollyRetrySettings GetSettings(string endpointName)
    {
        return Endpoints.TryGetValue(endpointName, out var settings) ? settings : Default;
    }
}
