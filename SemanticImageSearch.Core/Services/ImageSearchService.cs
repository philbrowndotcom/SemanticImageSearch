using SemanticImageSearch.Core.Interfaces;
using SemanticImageSearch.Core.Models;

namespace SemanticImageSearch.Core.Services;

public class ImageSearchService : IImageSearchService
{
    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".webp"] = "image/webp"
    };

    private readonly IVectorDatabaseService _vectorDatabase;
    private readonly ILLaVAEmbeddingService _embeddingService;

    public ImageSearchService(IVectorDatabaseService vectorDatabase, ILLaVAEmbeddingService embeddingService)
    {
        _vectorDatabase = vectorDatabase;
        _embeddingService = embeddingService;
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default)
    {
        var people = request.People?
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];

        var hasQuery = !string.IsNullOrWhiteSpace(request.Query);

        if (!hasQuery && people.Length == 0 && !request.From.HasValue && !request.To.HasValue)
        {
            throw new ArgumentException("Provide a query, a date range, or at least one person.");
        }

        if (request.From.HasValue && request.To.HasValue && request.From.Value.Date > request.To.Value.Date)
        {
            throw new ArgumentException("'from' must be on or before 'to'.");
        }

        // Filter-only searches do not need Ollama; generate an embedding only when a text query was supplied.
        // Must be the same embedding model the Indexer used.
        var queryVector = hasQuery ? await _embeddingService.GetTextEmbeddingAsync(request.Query!) : null;

        return await _vectorDatabase.SearchAsync(
            queryVector,
            Math.Clamp(request.Limit, 1, 100),
            request.From,
            request.To,
            people,
            cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetPeopleAsync(CancellationToken cancellationToken = default)
    {
        var people = await _vectorDatabase.GetPeopleNamesAsync(cancellationToken);
        return people.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    // Images are resolved by point id (never by raw path) to prevent arbitrary file access.
    public async Task<ImageFile?> GetImageFileAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var filePath = await _vectorDatabase.GetFilePathAsync(id, cancellationToken);

        if (string.IsNullOrWhiteSpace(filePath) ||
            !ContentTypes.TryGetValue(Path.GetExtension(filePath), out var contentType) ||
            !File.Exists(filePath))
        {
            return null;
        }

        return new ImageFile(filePath, contentType);
    }
}
