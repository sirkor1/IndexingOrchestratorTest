using SearchOrchestrator.Models;

namespace SearchOrchestrator.Services;

public interface IIndexingTaskRepository
{
    IndexingTask Add(IndexingTask task);
    IndexingTask Update(IndexingTask task);
    IndexingTask? GetById(Guid id);
    IReadOnlyList<IndexingTask> GetBySourceId(Guid sourceId);

    /// <summary>
    /// Returns an active (Pending or InProgress) task for the given source, if any.
    /// Used for idempotency checks.
    /// </summary>
    IndexingTask? GetActiveBySourceId(Guid sourceId);
}
