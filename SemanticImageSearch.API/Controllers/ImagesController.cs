using Microsoft.AspNetCore.Mvc;
using Qdrant.Client;

[ApiController]
[Route("api/[controller]")]
public class ImagesController : ControllerBase
{
    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".webp"] = "image/webp"
    };

    private readonly QdrantClient _qdrantClient;

    public ImagesController(QdrantClient qdrantClient)
    {
        _qdrantClient = qdrantClient;
    }

    // Images are resolved by point id (never by raw path) to prevent arbitrary file access.
    [HttpGet("{id:guid}")]
    [ResponseCache(Duration = 3600)]
    public async Task<IActionResult> Get(Guid id)
    {
        var points = await _qdrantClient.RetrieveAsync(
            collectionName: SearchController.CollectionName,
            id: id,
            withPayload: true,
            withVectors: false);

        var point = points.FirstOrDefault();
        if (point is null || !point.Payload.TryGetValue("file_path", out var pathValue))
        {
            return NotFound();
        }

        var filePath = pathValue.StringValue;
        if (string.IsNullOrWhiteSpace(filePath) ||
            !ContentTypes.TryGetValue(Path.GetExtension(filePath), out var contentType) ||
            !System.IO.File.Exists(filePath))
        {
            return NotFound();
        }

        return PhysicalFile(filePath, contentType, enableRangeProcessing: true);
    }
}
