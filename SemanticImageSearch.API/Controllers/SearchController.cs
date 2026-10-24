using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Qdrant.Client;
using SemanticImageSearch.Core.Options;
using Qdrant.Client.Grpc;
using System.Text.Json.Serialization;

[ApiController]
[Route("api/[controller]")]
public class SearchController : ControllerBase
{
    private readonly QdrantClient _qdrantClient;
    private readonly HttpClient _httpClient;
    private readonly QdrantOptions _qdrantOptions;
    private readonly OllamaOptions _ollamaOptions;
    public const string OllamaClientName = "ollama";

    public SearchController(
        QdrantClient qdrantClient,
        IHttpClientFactory httpClientFactory,
        IOptions<QdrantOptions> qdrantOptions,
        IOptions<OllamaOptions> ollamaOptions)
    {
        _qdrantClient = qdrantClient;
        _httpClient = httpClientFactory.CreateClient(OllamaClientName);
        _qdrantOptions = qdrantOptions.Value;
        _ollamaOptions = ollamaOptions.Value;
    }

    [HttpGet]
    public async Task<IActionResult> Search(
        [FromQuery] string? query,
        [FromQuery] int limit = 10,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] string[]? people = null)
    {
        var selectedPeople = people?
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];

        if (string.IsNullOrWhiteSpace(query) && selectedPeople.Length == 0 && !from.HasValue && !to.HasValue)
        {
            return BadRequest("Provide a query, a date range, or at least one person.");
        }

        if (from.HasValue && to.HasValue && from.Value.Date > to.Value.Date)
        {
            return BadRequest("'from' must be on or before 'to'.");
        }

        limit = Math.Clamp(limit, 1, 100);

        try
        {
            var filter = BuildFilter(from, to, selectedPeople);

            if (string.IsNullOrWhiteSpace(query))
            {
                // Filter-only searches do not need Ollama; return matching points in Qdrant order.
                var page = await _qdrantClient.ScrollAsync(
                    collectionName: _qdrantOptions.CollectionName,
                    limit: (uint)limit,
                    filter: filter,
                    payloadSelector: true,
                    vectorsSelector: false);

                return Ok(page.Result.Select(point => ToSearchResult(point.Id.Uuid, point.Payload, 0)));
            }

            // Generate an embedding only when a text query was supplied.
            float[] queryVector = await GetEmbeddingAsync(query);
            var searchResults = await _qdrantClient.QueryAsync(
                collectionName: _qdrantOptions.CollectionName,
                query: queryVector,
                filter: filter,
                payloadSelector: true,
                limit: (ulong)limit);

            return Ok(searchResults.Select(hit => ToSearchResult(hit.Id.Uuid, hit.Payload, hit.Score)));
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    private static Filter? BuildFilter(DateTime? from, DateTime? to, IReadOnlyCollection<string> people)
    {
        if (!from.HasValue && !to.HasValue && people.Count == 0)
        {
            return null;
        }

        var filter = new Filter();

        if (from.HasValue || to.HasValue)
        {
            var range = new DatetimeRange();
            if (from.HasValue)
            {
                range.Gte = ToTimestamp(from.Value.Date);
            }
            if (to.HasValue)
            {
                // Inclusive of the entire 'to' day
                range.Lt = ToTimestamp(to.Value.Date.AddDays(1));
            }

            filter.Must.Add(new Condition
            {
                Field = new FieldCondition { Key = "date_taken", DatetimeRange = range }
            });
        }

        foreach (var person in people)
        {
            filter.Must.Add(new Condition
            {
                Field = new FieldCondition
                {
                    Key = PeoplePayload.Key,
                    Match = new Match { Keyword = person }
                }
            });
        }

        return filter;
    }

    private static SearchResultDto ToSearchResult(string id, IDictionary<string, Value> payload, double score) =>
        new(id, GetString(payload, "file_path"), GetString(payload, "description"),
            PeoplePayload.Read(payload), GetString(payload, "date_taken"), score);

    // Dates from the UI are calendar days in the user's local time, matching how EXIF dates are interpreted.
    private static Google.Protobuf.WellKnownTypes.Timestamp ToTimestamp(DateTime localDate) =>
        Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(
            DateTime.SpecifyKind(localDate, DateTimeKind.Local).ToUniversalTime());

    private static string? GetString(IDictionary<string, Value> payload, string key) =>
        payload.TryGetValue(key, out var value) ? value.StringValue : null;

    private async Task<float[]> GetEmbeddingAsync(string text)
    {
        // Must be the same embedding model the Indexer used
        var requestPayload = new { model = _ollamaOptions.EmbeddingModel, prompt = text };

        var response = await _httpClient.PostAsJsonAsync("/api/embeddings", requestPayload);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OllamaEmbeddingResponse>();
        return result?.Embedding ?? throw new InvalidOperationException("Failed to generate embedding.");
    }

public sealed record SearchResultDto(
    string Id,
    string? FilePath,
    string? Description,
    string[] PeopleNames,
    string? DateTaken,
    double Score);
}

public class OllamaEmbeddingResponse
{
    [JsonPropertyName("embedding")]
    public float[] Embedding { get; set; }
}