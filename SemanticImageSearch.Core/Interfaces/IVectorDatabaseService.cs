using SemanticImageSearch.Core.Models;

namespace SemanticImageSearch.Core.Interfaces;

public interface IVectorDatabaseService
{
    Task EnsureCollectionExistsAsync(ulong vectorSize);
    Task UpsertImageAsync(ImageRecord record);
    Task<HashSet<string>> GetIndexedFilePathsAsync();

    /// <summary>
    /// Nearest-neighbour search when <paramref name="queryVector"/> is supplied; otherwise a filter-only scroll with a score of 0.
    /// </summary>
    Task<IReadOnlyList<SearchResult>> SearchAsync(
        float[]? queryVector,
        int limit,
        DateTime? from,
        DateTime? to,
        IReadOnlyCollection<string> people,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<string>> GetPeopleNamesAsync(CancellationToken cancellationToken = default);
    Task<string?> GetFilePathAsync(Guid id, CancellationToken cancellationToken = default);
}
