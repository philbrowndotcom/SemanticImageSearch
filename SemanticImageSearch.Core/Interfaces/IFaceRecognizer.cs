using SemanticImageSearch.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace SemanticImageSearch.Core.Interfaces;

public interface IFaceRecognizer
{
    /// <summary>
    /// Generates a 512-dimensional embedding vector for a detected face in an image.
    /// </summary>
    Task<float[]> GenerateFaceEmbeddingAsync(string imagePath, FaceBox face, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a 512-dimensional embedding vector from an already loaded ImageSharp image.
    /// </summary>
    float[] GenerateFaceEmbedding(Image<Rgb24> image, FaceBox face);

    /// <summary>
    /// Computes the cosine similarity between two 512-dimensional face embeddings (-1.0 to 1.0).
    /// Typically values above ~0.50 indicate the same person.
    /// </summary>
    float CompareFaces(float[] embeddingA, float[] embeddingB);
}

