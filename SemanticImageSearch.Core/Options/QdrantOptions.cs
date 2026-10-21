namespace SemanticImageSearch.Core.Options;

public class QdrantOptions
{
    public const string SectionName = "Qdrant";

    public string Host { get; set; } = "localhost";

    /// <summary>gRPC port used by the .NET client.</summary>
    public int Port { get; set; } = 6334;

    public string CollectionName { get; set; } = "image_library";

    /// <summary>Must match the output size of <see cref="OllamaOptions.EmbeddingModel"/> (768 for nomic-embed-text).</summary>
    public ulong VectorSize { get; set; } = 768;
}
