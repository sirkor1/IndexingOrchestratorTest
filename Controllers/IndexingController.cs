using Microsoft.AspNetCore.Mvc;
using SearchOrchestrator.Contracts;
using SearchOrchestrator.Middleware;
using SearchOrchestrator.Models;
using SearchOrchestrator.Services;

namespace SearchOrchestrator.Controllers;

[ApiController]
[Route("api/indexing/tasks")]
public class IndexingController : ControllerBase
{
    private readonly IndexingOrchestrator _orchestrator;
    private readonly CorrelationIdAccessor _correlationId;

    public IndexingController(IndexingOrchestrator orchestrator, CorrelationIdAccessor correlationId)
    {
        _orchestrator = orchestrator;
        _correlationId = correlationId;
    }

    /// <summary>
    /// Creates an indexing task for the given source.
    /// Idempotent: if an active task for the same source URI exists, returns it with 200.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(IndexingTaskResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(IndexingTaskResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateTask(
        [FromBody] CreateIndexingTaskRequest request,
        CancellationToken ct)
    {
        var (task, source, isExisting) = await _orchestrator.CreateTaskAsync(
            request, _correlationId.CorrelationId, ct);

        var response = MapToResponse(task, source);

        if (isExisting)
            return Ok(response);

        return CreatedAtAction(nameof(GetTask), new { taskId = task.Id }, response);
    }

    /// <summary>
    /// Returns the status of an indexing task.
    /// </summary>
    [HttpGet("{taskId:guid}")]
    [ProducesResponseType(typeof(IndexingTaskResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetTask(Guid taskId)
    {
        var (task, source) = _orchestrator.GetTask(taskId);
        if (task == null)
            return NotFound();

        return Ok(MapToResponse(task, source));
    }

    /// <summary>
    /// Cancels an active indexing task.
    /// Idempotent: cancelling an already-cancelled task returns 200.
    /// Returns 409 if the task is in a terminal state (Completed, Failed).
    /// </summary>
    [HttpPost("{taskId:guid}/cancel")]
    [ProducesResponseType(typeof(IndexingTaskResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CancelTask(Guid taskId, CancellationToken ct)
    {
        var (task, source) = await _orchestrator.CancelTaskAsync(taskId, ct);
        if (task == null)
            return NotFound();

        if (task.Status is IndexingTaskStatus.Completed or IndexingTaskStatus.Failed)
        {
            return Conflict(new
            {
                error = $"Cannot cancel task in {task.Status} status",
                task = MapToResponse(task, source)
            });
        }

        return Ok(MapToResponse(task, source));
    }

    private static IndexingTaskResponse MapToResponse(IndexingTask task, IndexingSource? source) => new()
    {
        TaskId = task.Id,
        SourceId = task.SourceId,
        SourceUri = source?.Uri ?? string.Empty,
        SourceName = source?.Name ?? string.Empty,
        Status = task.Status.ToString(),
        ErrorMessage = task.ErrorMessage,
        CorrelationId = task.CorrelationId,
        CreatedAt = task.CreatedAt,
        StartedAt = task.StartedAt,
        CompletedAt = task.CompletedAt
    };
}
