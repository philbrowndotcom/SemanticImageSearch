using SemanticImageSearch.Core.Models;

namespace SemanticImageSearch.Core.Interfaces;

public interface IImageSearchService
{
    /// <exception cref="ArgumentException">The request has no query and no filter, or 'from' is after 'to'.</exception>
    Task<IReadOnlyList<SearchResult>> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetPeopleAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns null when the image is unknown, not a supported type, or no longer on disk.</summary>
    Task<ImageFile?> GetImageFileAsync(Guid id, CancellationToken cancellationToken = default);
}
