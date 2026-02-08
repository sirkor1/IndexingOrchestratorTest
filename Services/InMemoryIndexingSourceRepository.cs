using System.Collections.Concurrent;
using SearchOrchestrator.Models;

namespace SearchOrchestrator.Services;

public class InMemoryIndexingSourceRepository : IIndexingSourceRepository
{
    private readonly ConcurrentDictionary<Guid, IndexingSource> _sources = new();

    public IndexingSource Add(IndexingSource source)
    {
        _sources[source.Id] = source;
        return source;
    }

    public IndexingSource? GetById(Guid id) =>
        _sources.GetValueOrDefault(id);

    public IndexingSource? GetByUri(string uri) =>
        _sources.Values.FirstOrDefault(s => s.Uri == uri);
}
