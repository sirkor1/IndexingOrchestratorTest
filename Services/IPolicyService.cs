using Polly;

namespace SearchOrchestrator.Services;

public interface IPolicyService
{
    /// <summary>
    /// Returns a retry policy for the given endpoint.
    /// Optionally accepts a custom rule to add extra result-based retry conditions.
    /// </summary>
    AsyncPolicy<HttpResponseMessage> GetRetryPolicy(
        string endpointName = "Default",
        Func<HttpResponseMessage, bool>? customRule = null);

    /// <summary>
    /// Returns a circuit breaker policy for the given endpoint.
    /// Each endpoint gets its own isolated circuit breaker instance.
    /// </summary>
    AsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy(string endpointName);

    /// <summary>
    /// Returns a combined policy with retry and circuit breaker.
    /// </summary>
    AsyncPolicy<HttpResponseMessage> GetCombinedPolicy(string endpointName = "Default");
}
