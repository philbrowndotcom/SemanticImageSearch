namespace SemanticImageSearch.Core.Interfaces;

public record FaceMatchResult(string Name, float Similarity, bool IsMatch = true);

public interface IFaceRegistry
{
    Task LoadReferencesAsync(string referenceDirectoryPath, CancellationToken cancellationToken = default);
    FaceMatchResult? Match(float[] faceEmbedding, float threshold = 0.45f);
    FaceMatchResult? FindClosest(float[] faceEmbedding);
    IReadOnlyDictionary<string, List<float[]>> KnownPeople { get; }
}
