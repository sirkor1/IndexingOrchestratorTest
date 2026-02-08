using Microsoft.AspNetCore.Mvc;
using SearchOrchestrator.Contracts;
using SearchOrchestrator.Middleware;
using SearchOrchestrator.Services;

namespace SearchOrchestrator.Controllers;

[ApiController]
[Route("api/search")]
public class SearchController : ControllerBase
{
    private readonly IndexingOrchestrator _orchestrator;
    private readonly ILogger<SearchController> _logger;
    private readonly CorrelationIdAccessor _correlationId;

    public SearchController(
        IndexingOrchestrator orchestrator,
        ILogger<SearchController> logger,
        CorrelationIdAccessor correlationId)
    {
        _orchestrator = orchestrator;
        _logger = logger;
        _correlationId = correlationId;
    }

    /// <summary>
    /// Performs a search query against the external search engine.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(SearchResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Search(
        [FromBody] SearchRequest request,
        CancellationToken ct)
    {
        _logger.LogInformation("Search request received: {SearchQuery}, MaxResults {MaxResults}, CorrelationId {CorrelationId}",
            request.Query, request.MaxResults, _correlationId.CorrelationId);

        var result = await _orchestrator.SearchAsync(request, _correlationId.CorrelationId, ct);

        if (!result.IsSuccess)
        {
            _logger.LogWarning("Search failed: {SearchQuery}, Error {Error}, CorrelationId {CorrelationId}",
                request.Query, result.Error, _correlationId.CorrelationId);

            return StatusCode(StatusCodes.Status502BadGateway,
                new { error = result.Error });
        }

        return Ok(result.Value);
    }
}
