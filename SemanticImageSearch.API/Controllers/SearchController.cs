using Microsoft.AspNetCore.Mvc;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using System.Text.Json.Serialization;

[ApiController]
[Route("api/[controller]")]
public class SearchController : ControllerBase
{
    private readonly QdrantClient _qdrantClient;
    private readonly HttpClient _httpClient;
    public const string CollectionName = "image_library"; // Must match QdrantVectorService collection name
    public const string OllamaClientName = "ollama";

    public SearchController(QdrantClient qdrantClient, IHttpClientFactory httpClientFactory)
    {
        _qdrantClient = qdrantClient;
        _httpClient = httpClientFactory.CreateClient(OllamaClientName);
    }

    [HttpGet]
    public async Task<IActionResult> Search(
        [FromQuery] string query,
        [FromQuery] int limit = 10,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return BadRequest("Query parameter is required.");
        }

        if (from.HasValue && to.HasValue && from.Value.Date > to.Value.Date)
        {
            return BadRequest("'from' must be on or before 'to'.");
        }

        limit = Math.Clamp(limit, 1, 100);

        try
        {
            // 1. Generate text embedding for the search query via Ollama
            float[] queryVector = await GetEmbeddingAsync(query);

            // 2. Search Qdrant for nearest neighbor vectors, optionally filtered by date taken
            var searchResults = await _qdrantClient.QueryAsync(
                collectionName: CollectionName,
                query: queryVector,
                filter: BuildDateTakenFilter(from, to),
                payloadSelector: true,
                limit: (ulong)limit
            );

            // 3. Map results to a clean response format
            var results = searchResults.Select(hit => new
            {
                Id = hit.Id.Uuid,
                FilePath = GetString(hit.Payload, "file_path"),
                Description = GetString(hit.Payload, "description"),
                DateTaken = GetString(hit.Payload, "date_taken"),
                Score = hit.Score
            });

            return Ok(results);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    private static Filter? BuildDateTakenFilter(DateTime? from, DateTime? to)
    {
        if (!from.HasValue && !to.HasValue)
        {
            return null;
        }

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

        return new Filter
        {
            Must =
            {
                new Condition
                {
                    Field = new FieldCondition { Key = "date_taken", DatetimeRange = range }
                }
            }
        };
    }

    // Dates from the UI are calendar days in the user's local time, matching how EXIF dates are interpreted.
    private static Google.Protobuf.WellKnownTypes.Timestamp ToTimestamp(DateTime localDate) =>
        Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(
            DateTime.SpecifyKind(localDate, DateTimeKind.Local).ToUniversalTime());

    private static string? GetString(IDictionary<string, Value> payload, string key) =>
        payload.TryGetValue(key, out var value) ? value.StringValue : null;

    private async Task<float[]> GetEmbeddingAsync(string text)
    {
        // Adjust model name if you use a dedicated embedding model in Ollama (e.g., "nomic-embed-text")
        var requestPayload = new { model = "nomic-embed-text", prompt = text };

        var response = await _httpClient.PostAsJsonAsync("/api/embeddings", requestPayload);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OllamaEmbeddingResponse>();
        return result?.Embedding ?? throw new InvalidOperationException("Failed to generate embedding.");
    }
}

public class OllamaEmbeddingResponse
{
    [JsonPropertyName("embedding")]
    public float[] Embedding { get; set; }
}