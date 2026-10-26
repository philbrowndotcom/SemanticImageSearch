using System.Security.Cryptography;
using System.Text;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using SemanticImageSearch.Core.Exceptions;
using SemanticImageSearch.Core.Interfaces;
using SemanticImageSearch.Core.Models;
using SemanticImageSearch.Core.Options;

namespace SemanticImageSearch.Core.Services;

public class QdrantVectorService : IVectorDatabaseService
{
    private const string FilePathKey = "file_path";
    private const string DescriptionKey = "description";
    private const string PeopleNamesKey = "people_names";
    private const string DateTakenKey = "date_taken";

    private readonly QdrantClient _client;
    private readonly string CollectionName;
    private readonly string _endpoint;

    public QdrantVectorService(QdrantOptions options)
    {
        _client = new QdrantClient(options.Host, options.Port);
        CollectionName = options.CollectionName;
        _endpoint = $"{options.Host}:{options.Port}";
    }

    private async Task<T> RunAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (Grpc.Core.RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.Unavailable)
        {
            // Unavailable means the connection itself failed (Qdrant not running, wrong host or port).
            throw new VectorDatabaseUnavailableException(
                $"Cannot reach Qdrant at {_endpoint}. Make sure Qdrant is running (for example, start its Docker container).", ex);
        }
    }

    private Task RunAsync(Func<Task> action) => RunAsync(async () =>
    {
        await action();
        return true;
    });

    public Task EnsureCollectionExistsAsync(ulong vectorSize) => RunAsync(async () =>
    {
        var collections = await _client.ListCollectionsAsync();

        if (!collections.Contains(CollectionName))
        {
            await _client.CreateCollectionAsync(
                collectionName: CollectionName,
                vectorsConfig: new VectorParams
                {
                    Size = vectorSize, // 768 for nomic-embed-text
                    Distance = Distance.Cosine
                });
        }

        try
        {
            await _client.CreatePayloadIndexAsync(
                collectionName: CollectionName,
                fieldName: DateTakenKey,
                schemaType: PayloadSchemaType.Datetime);
        }
        catch (Grpc.Core.RpcException ex) when (ex.StatusCode != Grpc.Core.StatusCode.Unavailable)
        {
            // Index already exists or cannot be created; range filters still work without it, just slower.
        }

        try
        {
            await _client.CreatePayloadIndexAsync(
                collectionName: CollectionName,
                fieldName: PeopleNamesKey,
                schemaType: PayloadSchemaType.Keyword);
        }
        catch (Grpc.Core.RpcException ex) when (ex.StatusCode != Grpc.Core.StatusCode.Unavailable)
        {
            // Index already exists or cannot be created; people filters still work without it, just slower.
        }
    });

    public Task UpsertImageAsync(ImageRecord record) => RunAsync(async () =>
    {
        var pointId = CreatePointId(record.FilePath);

        await _client.UpsertAsync(
            collectionName: CollectionName,
            points: new List<PointStruct>
            {
                new PointStruct
                {
                    Id = pointId,
                    Vectors = record.Vector,
                    Payload =
                    {
                        [FilePathKey] = record.FilePath,
                        [DescriptionKey] = record.Description,
                        [PeopleNamesKey] = new Value
                        {
                            ListValue = new ListValue
                            {
                                Values = { record.PeopleNames.Select(name => new Value { StringValue = name }) }
                            }
                        },
                        ["last_modified_utc"] = record.LastModifiedUtc.ToString("o"),
                        [DateTakenKey] = record.DateTaken.ToString("o")
                    }
                }
            });
    });

    public Task<HashSet<string>> GetIndexedFilePathsAsync() => RunAsync(async () =>
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        PointId? offset = null;

        do
        {
            var page = await _client.ScrollAsync(
                collectionName: CollectionName,
                limit: 1000,
                offset: offset,
                payloadSelector: new WithPayloadSelector
                {
                    Include = new PayloadIncludeSelector { Fields = { FilePathKey } }
                },
                vectorsSelector: false);

            foreach (var point in page.Result)
            {
                if (point.Payload.TryGetValue(FilePathKey, out var value) && !string.IsNullOrEmpty(value.StringValue))
                {
                    paths.Add(value.StringValue);
                }
            }

            offset = page.NextPageOffset;
        }
        while (offset is not null);

        return paths;
    });

    public Task<IReadOnlyList<SearchResult>> SearchAsync(
        float[]? queryVector,
        int limit,
        DateTime? from,
        DateTime? to,
        IReadOnlyCollection<string> people,
        CancellationToken cancellationToken = default) => RunAsync<IReadOnlyList<SearchResult>>(async () =>
    {
        var filter = BuildFilter(from, to, people);

        if (queryVector is null)
        {
            // Filter-only searches are not ranked; return matching points in Qdrant order.
            var page = await _client.ScrollAsync(
                collectionName: CollectionName,
                limit: (uint)limit,
                filter: filter,
                payloadSelector: true,
                vectorsSelector: false,
                cancellationToken: cancellationToken);

            return page.Result.Select(point => ToSearchResult(point.Id.Uuid, point.Payload, 0)).ToArray();
        }

        var hits = await _client.QueryAsync(
            collectionName: CollectionName,
            query: queryVector,
            filter: filter,
            payloadSelector: true,
            limit: (ulong)limit,
            cancellationToken: cancellationToken);

        return hits.Select(hit => ToSearchResult(hit.Id.Uuid, hit.Payload, hit.Score)).ToArray();
    });

    public Task<IReadOnlyCollection<string>> GetPeopleNamesAsync(CancellationToken cancellationToken = default) => RunAsync<IReadOnlyCollection<string>>(async () =>
    {
        // Case-sensitive so every listed name matches the exact keyword filter used by search.
        var people = new HashSet<string>(StringComparer.Ordinal);
        PointId? offset = null;

        do
        {
            var page = await _client.ScrollAsync(
                collectionName: CollectionName,
                limit: 1000,
                offset: offset,
                payloadSelector: new WithPayloadSelector
                {
                    Include = new PayloadIncludeSelector { Fields = { PeopleNamesKey } }
                },
                vectorsSelector: false,
                cancellationToken: cancellationToken);

            foreach (var point in page.Result)
            {
                people.UnionWith(ReadPeopleNames(point.Payload));
            }

            offset = page.NextPageOffset;
        }
        while (offset is not null);

        return people;
    });

    public Task<string?> GetFilePathAsync(Guid id, CancellationToken cancellationToken = default) => RunAsync(async () =>
    {
        var points = await _client.RetrieveAsync(
            collectionName: CollectionName,
            id: id,
            withPayload: true,
            withVectors: false,
            cancellationToken: cancellationToken);

        var point = points.FirstOrDefault();
        return point is null ? null : GetString(point.Payload, FilePathKey);
    });

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
                Field = new FieldCondition { Key = DateTakenKey, DatetimeRange = range }
            });
        }

        foreach (var person in people)
        {
            filter.Must.Add(new Condition
            {
                Field = new FieldCondition
                {
                    Key = PeopleNamesKey,
                    Match = new Match { Keyword = person }
                }
            });
        }

        return filter;
    }

    // Dates from the UI are calendar days in the user's local time, matching how EXIF dates are interpreted.
    private static Google.Protobuf.WellKnownTypes.Timestamp ToTimestamp(DateTime localDate) =>
        Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(
            DateTime.SpecifyKind(localDate, DateTimeKind.Local).ToUniversalTime());

    private static SearchResult ToSearchResult(string id, IDictionary<string, Value> payload, double score) =>
        new(id, GetString(payload, FilePathKey), GetString(payload, DescriptionKey),
            ReadPeopleNames(payload), GetString(payload, DateTakenKey), score);

    private static string? GetString(IDictionary<string, Value> payload, string key) =>
        payload.TryGetValue(key, out var value) ? value.StringValue : null;

    // Names are returned exactly as stored so they can be used as-is in the keyword filter.
    private static string[] ReadPeopleNames(IDictionary<string, Value> payload) =>
        payload.TryGetValue(PeopleNamesKey, out var value) && value.ListValue is not null
            ? value.ListValue.Values
                .Select(item => item.StringValue)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToArray()
            : [];

    // Same file path always maps to the same point id, so re-upserting a file overwrites instead of duplicating.
    private static Guid CreatePointId(string filePath)
    {
        var normalized = Path.GetFullPath(filePath).ToUpperInvariant();
        return new Guid(MD5.HashData(Encoding.UTF8.GetBytes(normalized)));
    }
}
