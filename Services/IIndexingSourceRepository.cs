using SearchOrchestrator.Models;

namespace SearchOrchestrator.Services;

public interface IIndexingSourceRepository
{
    IndexingSource Add(IndexingSource source);
    IndexingSource? GetById(Guid id);
    IndexingSource? GetByUri(string uri);
}
