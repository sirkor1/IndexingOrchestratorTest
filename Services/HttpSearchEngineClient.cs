using System.Net.Http.Json;
using SearchOrchestrator.Configuration;
using SearchOrchestrator.Models;

namespace SearchOrchestrator.Services;

/// <summary>
/// HTTP-based implementation of the external search engine client.
/// Uses IHttpClientFactory for proper handler lifecycle management.
/// Retry policies are applied at the call site via IPolicyService.
/// </summary>
public class HttpSearchEngineClient : ISearchEngineClient
{
    private readonly IHttpClientFactory _clientFactory;
    private readonly IPolicyService _policyService;
    private readonly ILogger<HttpSearchEngineClient> _logger;

    public HttpSearchEngineClient(
        IHttpClientFactory clientFactory,
        IPolicyService policyService,
        ILogger<HttpSearchEngineClient> logger)
    {
        _clientFactory = clientFactory;
        _policyService = policyService;
        _logger = logger;
    }

    public async Task<Result<ExternalIndexingResult>> StartIndexingAsync(string sourceUri, CancellationToken ct)
    {
        var client = _clientFactory.CreateClient(HttpClientNames.SearchEngine);
        var policy = _policyService.GetCombinedPolicy(PolicyEndpoints.StartIndexing);

        var response = await policy.ExecuteAsync(
            () => client.PostAsJsonAsync("indexing/start", new { sourceUri }, ct));

        SearchEngineMetrics.HttpRequestsTotal
            .WithLabels("StartIndexing", ((int)response.StatusCode).ToString()).Inc();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("StartIndexing returned {StatusCode}", (int)response.StatusCode);
            SearchEngineMetrics.HttpErrorsTotal
                .WithLabels("StartIndexing", ((int)response.StatusCode).ToString()).Inc();
            return Result<ExternalIndexingResult>.Failure(
                $"StartIndexing failed with status {(int)response.StatusCode}",
                response.StatusCode);
        }

        var result = await response.Content.ReadFromJsonAsync<ExternalIndexingResult>(ct);
        if (result == null)
            return Result<ExternalIndexingResult>.Failure("Failed to deserialize StartIndexing response");

        return Result<ExternalIndexingResult>.Success(result);
    }

    public async Task<Result<ExternalIndexingStatus>> CheckIndexingStatusAsync(string externalTaskId, CancellationToken ct)
    {
        var client = _clientFactory.CreateClient(HttpClientNames.SearchEngine);
        var policy = _policyService.GetCombinedPolicy(PolicyEndpoints.CheckStatus);

        var response = await policy.ExecuteAsync(
            () => client.GetAsync($"indexing/status/{externalTaskId}", ct));

        SearchEngineMetrics.HttpRequestsTotal
            .WithLabels("CheckStatus", ((int)response.StatusCode).ToString()).Inc();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("CheckStatus returned {StatusCode}", (int)response.StatusCode);
            SearchEngineMetrics.HttpErrorsTotal
                .WithLabels("CheckStatus", ((int)response.StatusCode).ToString()).Inc();
            return Result<ExternalIndexingStatus>.Failure(
                $"CheckStatus failed with status {(int)response.StatusCode}",
                response.StatusCode);
        }

        var result = await response.Content.ReadFromJsonAsync<ExternalIndexingStatus>(ct);
        if (result == null)
            return Result<ExternalIndexingStatus>.Failure("Failed to deserialize CheckIndexingStatus response");

        return Result<ExternalIndexingStatus>.Success(result);
    }

    public async Task<Result> CancelIndexingAsync(string externalTaskId, CancellationToken ct)
    {
        var client = _clientFactory.CreateClient(HttpClientNames.SearchEngine);
        var policy = _policyService.GetCombinedPolicy(PolicyEndpoints.CancelIndexing);

        var response = await policy.ExecuteAsync(
            () => client.PostAsync($"indexing/cancel/{externalTaskId}", null, ct));

        SearchEngineMetrics.HttpRequestsTotal
            .WithLabels("CancelIndexing", ((int)response.StatusCode).ToString()).Inc();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("CancelIndexing returned {StatusCode}", (int)response.StatusCode);
            SearchEngineMetrics.HttpErrorsTotal
                .WithLabels("CancelIndexing", ((int)response.StatusCode).ToString()).Inc();
            return Result.Failure(
                $"CancelIndexing failed with status {(int)response.StatusCode}",
                response.StatusCode);
        }

        return Result.Success();
    }

    public async Task<Result<ExternalSearchResult>> SearchAsync(string query, int maxResults, CancellationToken ct)
    {
        var client = _clientFactory.CreateClient(HttpClientNames.SearchEngine);
        var policy = _policyService.GetCombinedPolicy(PolicyEndpoints.Search);

        var response = await policy.ExecuteAsync(
            () => client.PostAsJsonAsync("search", new { query, maxResults }, ct));

        SearchEngineMetrics.HttpRequestsTotal
            .WithLabels("Search", ((int)response.StatusCode).ToString()).Inc();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Search returned {StatusCode}", (int)response.StatusCode);
            SearchEngineMetrics.HttpErrorsTotal
                .WithLabels("Search", ((int)response.StatusCode).ToString()).Inc();
            return Result<ExternalSearchResult>.Failure(
                $"Search failed with status {(int)response.StatusCode}",
                response.StatusCode);
        }

        var result = await response.Content.ReadFromJsonAsync<ExternalSearchResult>(ct);
        if (result == null)
            return Result<ExternalSearchResult>.Failure("Failed to deserialize Search response");

        return Result<ExternalSearchResult>.Success(result);
    }
}
