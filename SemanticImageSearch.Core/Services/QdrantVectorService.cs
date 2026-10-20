using Qdrant.Client;
using Qdrant.Client.Grpc;
using SemanticImageSearch.Core.Interfaces;
using SemanticImageSearch.Core.Models;

namespace SemanticImageSearch.Core.Services;

public class QdrantVectorService : IVectorDatabaseService
{
    private readonly QdrantClient _client;
    private const string CollectionName = "image_library";

    public QdrantVectorService(string host = "localhost", int port = 6334)
    {
        _client = new QdrantClient(host, port);
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
        var pointId = Guid.NewGuid();

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
                        ["last_modified_utc"] = record.LastModifiedUtc.ToString("o"),
                        ["date_taken"] = record.DateTaken.ToString("o")
                    }
                }
            });
    }
}