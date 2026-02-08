using System.Collections.Concurrent;
using SearchOrchestrator.Models;

namespace SearchOrchestrator.Services;

public class InMemoryIndexingTaskRepository : IIndexingTaskRepository
{
    private readonly ConcurrentDictionary<Guid, IndexingTask> _tasks = new();

    public IndexingTask Add(IndexingTask task)
    {
        _tasks[task.Id] = task;
        return task;
    }

    public IndexingTask Update(IndexingTask task)
    {
        _tasks[task.Id] = task;
        return task;
    }

    public IndexingTask? GetById(Guid id) =>
        _tasks.GetValueOrDefault(id);

    public IReadOnlyList<IndexingTask> GetBySourceId(Guid sourceId) =>
        _tasks.Values
            .Where(t => t.SourceId == sourceId)
            .OrderByDescending(t => t.CreatedAt)
            .ToList();

    public IndexingTask? GetActiveBySourceId(Guid sourceId) =>
        _tasks.Values.FirstOrDefault(t =>
            t.SourceId == sourceId &&
            t.Status is IndexingTaskStatus.Pending or IndexingTaskStatus.InProgress);
}
