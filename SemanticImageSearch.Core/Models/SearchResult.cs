namespace SemanticImageSearch.Core.Models;

public sealed record SearchResult(
    string Id,
    string? FilePath,
    string? Description,
    string[] PeopleNames,
    string? DateTaken,
    double Score);
