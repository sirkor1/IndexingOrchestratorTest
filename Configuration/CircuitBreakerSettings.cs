namespace SearchOrchestrator.Configuration;

/// <summary>
/// Circuit breaker configuration.
/// Supports both count-based and percentage-based (advanced) circuit breakers.
/// </summary>
public class CircuitBreakerSettings
{
    /// <summary>
    /// Enable circuit breaker.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Use advanced circuit breaker (percentage-based) instead of simple (count-based).
    /// </summary>
    public bool UseAdvanced { get; set; } = true;

    // === Simple Circuit Breaker (count-based) ===

    /// <summary>
    /// Number of consecutive failures before opening the circuit (simple mode).
    /// </summary>
    public int FailureThreshold { get; set; } = 5;

    // === Advanced Circuit Breaker (percentage-based) ===

    /// <summary>
    /// Failure rate threshold (0.0 to 1.0) to open the circuit (advanced mode).
    /// Example: 0.5 = 50% of requests must fail.
    /// </summary>
    public double FailureRateThreshold { get; set; } = 0.5;

    /// <summary>
    /// Minimum number of requests in the sampling window before circuit breaker can open (advanced mode).
    /// Prevents opening on too few requests.
    /// </summary>
    public int MinimumThroughput { get; set; } = 10;

    /// <summary>
    /// Duration of the sampling window in seconds (advanced mode).
    /// Circuit breaker calculates failure rate over this period.
    /// </summary>
    public int SamplingDurationSeconds { get; set; } = 30;

    // === Common Settings ===

    /// <summary>
    /// Duration in seconds the circuit stays open before attempting to close.
    /// </summary>
    public int DurationOfBreakSeconds { get; set; } = 30;
}
