namespace SemanticImageSearch.Core;

public record FaceBox(
    float X1,
    float Y1,
    float X2,
    float Y2,
    float Confidence,
    float[]? Landmarks = null,
    string? Name = null,
    float? Similarity = null
);
