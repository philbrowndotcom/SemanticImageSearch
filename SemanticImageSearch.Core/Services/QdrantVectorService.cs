using System.Security.Cryptography;
using System.Text;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using SemanticImageSearch.Core.Interfaces;
using SemanticImageSearch.Core.Models;
using SemanticImageSearch.Core.Options;

namespace SemanticImageSearch.Core.Services;

public class QdrantVectorService : IVectorDatabaseService
{
    private readonly QdrantClient _client;
    private readonly string CollectionName;

    public QdrantVectorService(QdrantOptions options)
    {
        _client = new QdrantClient(options.Host, options.Port);
        CollectionName = options.CollectionName;
    }

    public async Task EnsureCollectionExistsAsync(ulong vectorSize)
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
                fieldName: "date_taken",
                schemaType: PayloadSchemaType.Datetime);
        }
        catch (Grpc.Core.RpcException)
        {
            // Index already exists or cannot be created; range filters still work without it, just slower.
        }
    }

    public async Task UpsertImageAsync(ImageRecord record)
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
                        ["file_path"] = record.FilePath,
                        ["description"] = record.Description,
                        ["people_names"] = new Value
                        {
                            ListValue = new ListValue
                            {
                                Values = { record.PeopleNames.Select(name => new Value { StringValue = name }) }
                            }
                        },
                        ["last_modified_utc"] = record.LastModifiedUtc.ToString("o"),
                        ["date_taken"] = record.DateTaken.ToString("o")
                    }
                }
            });
    }

    public async Task<HashSet<string>> GetIndexedFilePathsAsync()
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
                    Include = new PayloadIncludeSelector { Fields = { "file_path" } }
                },
                vectorsSelector: false);

            foreach (var point in page.Result)
            {
                if (point.Payload.TryGetValue("file_path", out var value) && !string.IsNullOrEmpty(value.StringValue))
                {
                    paths.Add(value.StringValue);
                }
            }

            offset = page.NextPageOffset;
        }
        while (offset is not null);

        return paths;
    }

    // Same file path always maps to the same point id, so re-upserting a file overwrites instead of duplicating.
    private static Guid CreatePointId(string filePath)
    {
        var normalized = Path.GetFullPath(filePath).ToUpperInvariant();
        return new Guid(MD5.HashData(Encoding.UTF8.GetBytes(normalized)));
    }
}