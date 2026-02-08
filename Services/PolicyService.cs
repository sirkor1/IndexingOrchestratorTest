using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Options;
using Polly;
using SearchOrchestrator.Configuration;

namespace SearchOrchestrator.Services;

public class PolicyService : IPolicyService
{
    private readonly PolicySettings _settings;
    private readonly ILogger<PolicyService> _logger;
    private readonly ConcurrentDictionary<string, AsyncPolicy<HttpResponseMessage>> _circuitBreakers = new();
    private readonly ConcurrentDictionary<string, AsyncPolicy<HttpResponseMessage>> _combinedPolicies = new();

    public PolicyService(IOptions<PolicySettings> settings, ILogger<PolicyService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public AsyncPolicy<HttpResponseMessage> GetRetryPolicy(
        string endpointName = "Default",
        Func<HttpResponseMessage, bool>? customRule = null)
    {
        var retrySettings = _settings.GetSettings(endpointName);

        if (!retrySettings.Enabled)
            return Policy.NoOpAsync<HttpResponseMessage>();

        var timeRetrySpan = ParseRetryDelays(retrySettings.RetryDelays);

        var defaultPolicy = Policy
            .HandleResult<HttpResponseMessage>(IsTransientError)
            .Or<HttpRequestException>();

        if (customRule != null)
            defaultPolicy = defaultPolicy.OrResult(customRule);

        return defaultPolicy.WaitAndRetryAsync(
            timeRetrySpan,
            onRetry: (outcome, delay, attempt, _) =>
            {
                _logger.LogWarning(outcome.Exception,
                    "Retrying HTTP request to search engine: Endpoint {PolicyEndpoint}, Attempt {Attempt}, Delay {RetryDelay}ms, StatusCode {StatusCode}, Error {ErrorMessage}",
                    endpointName,
                    attempt,
                    delay.TotalMilliseconds,
                    outcome.Result?.StatusCode.ToString() ?? "N/A",
                    outcome.Exception?.Message ?? string.Empty);
            });
    }

    private static bool IsTransientError(HttpResponseMessage response)
    {
        return response.StatusCode >= HttpStatusCode.InternalServerError
               || response.StatusCode == HttpStatusCode.RequestTimeout;
    }

    /// <summary>
    /// Returns a cached circuit breaker policy for the given endpoint.
    /// Each endpoint gets its own isolated instance to prevent cascading failures.
    /// </summary>
    public AsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy(string endpointName)
    {
        return _circuitBreakers.GetOrAdd(endpointName, name => CreateCircuitBreakerPolicy(name));
    }

    private AsyncPolicy<HttpResponseMessage> CreateCircuitBreakerPolicy(string endpointName)
    {
        var settings = _settings.CircuitBreaker;

        if (!settings.Enabled)
            return Policy.NoOpAsync<HttpResponseMessage>();

        var basePolicy = Policy
            .HandleResult<HttpResponseMessage>(r => !r.IsSuccessStatusCode)
            .Or<HttpRequestException>();

        var durationOfBreak = TimeSpan.FromSeconds(settings.DurationOfBreakSeconds);

        if (settings.UseAdvanced)
        {
            _logger.LogInformation(
                "Circuit breaker [{Endpoint}] configured in ADVANCED mode: {FailureRate}% failures over {MinThroughput} requests in {SamplingWindow}s window",
                endpointName,
                settings.FailureRateThreshold * 100,
                settings.MinimumThroughput,
                settings.SamplingDurationSeconds);

            return basePolicy.AdvancedCircuitBreakerAsync(
                failureThreshold: settings.FailureRateThreshold,
                samplingDuration: TimeSpan.FromSeconds(settings.SamplingDurationSeconds),
                minimumThroughput: settings.MinimumThroughput,
                durationOfBreak: durationOfBreak,
                onBreak: (outcome, duration) =>
                {
                    _logger.LogWarning(
                        "Circuit breaker [{Endpoint}] OPENED for {DurationSeconds}s. StatusCode: {StatusCode}, Error: {ErrorMessage}",
                        endpointName,
                        duration.TotalSeconds,
                        outcome.Result?.StatusCode.ToString() ?? "N/A",
                        outcome.Exception?.Message ?? "N/A");
                },
                onReset: () => _logger.LogInformation("Circuit breaker [{Endpoint}] RESET (closed)", endpointName),
                onHalfOpen: () => _logger.LogInformation("Circuit breaker [{Endpoint}] HALF-OPEN (testing connection)", endpointName));
        }
        else
        {
            _logger.LogInformation(
                "Circuit breaker [{Endpoint}] configured in SIMPLE mode: {FailureThreshold} consecutive failures",
                endpointName,
                settings.FailureThreshold);

            return basePolicy.CircuitBreakerAsync(
                handledEventsAllowedBeforeBreaking: settings.FailureThreshold,
                durationOfBreak: durationOfBreak,
                onBreak: (outcome, duration) =>
                {
                    _logger.LogWarning(
                        "Circuit breaker [{Endpoint}] OPENED for {DurationSeconds}s. StatusCode: {StatusCode}, Error: {ErrorMessage}",
                        endpointName,
                        duration.TotalSeconds,
                        outcome.Result?.StatusCode.ToString() ?? "N/A",
                        outcome.Exception?.Message ?? "N/A");
                },
                onReset: () => _logger.LogInformation("Circuit breaker [{Endpoint}] RESET (closed)", endpointName),
                onHalfOpen: () => _logger.LogInformation("Circuit breaker [{Endpoint}] HALF-OPEN (testing connection)", endpointName));
        }
    }

    /// <summary>
    /// Returns a cached combined policy (retry + circuit breaker) for the given endpoint.
    /// Policies are created once per endpoint and reused to preserve circuit breaker state.
    /// </summary>
    public AsyncPolicy<HttpResponseMessage> GetCombinedPolicy(string endpointName = "Default")
    {
        return _combinedPolicies.GetOrAdd(endpointName, name =>
        {
            var retryPolicy = GetRetryPolicy(name);
            var circuitBreakerPolicy = GetCircuitBreakerPolicy(name);

            // Circuit breaker wraps retry policy
            return Policy.WrapAsync(circuitBreakerPolicy, retryPolicy);
        });
    }

    private static List<TimeSpan> ParseRetryDelays(string retryDelays)
    {
        try
        {
            return retryDelays
                .Split(',')
                .Select(s => TimeSpan.FromSeconds(double.Parse(s.Trim())))
                .ToList();
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException(
                $"Invalid retry delay configuration: '{retryDelays}'. Expected comma-separated numbers.", ex);
        }
        catch (OverflowException ex)
        {
            throw new InvalidOperationException(
                $"Retry delay value is too large: '{retryDelays}'.", ex);
        }
    }
}
