namespace SemanticImageSearch.Core.Models;

public sealed record SearchRequest(
    string? Query,
    int Limit = 10,
    DateTime? From = null,
    DateTime? To = null,
    string[]? People = null);
