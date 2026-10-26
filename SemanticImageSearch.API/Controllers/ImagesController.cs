using Microsoft.AspNetCore.Mvc;
using SemanticImageSearch.Core.Interfaces;

[ApiController]
[Route("api/[controller]")]
public class ImagesController : ControllerBase
{
    private readonly IImageSearchService _searchService;

    public ImagesController(IImageSearchService searchService)
    {
        _searchService = searchService;
    }

    [HttpGet("{id:guid}")]
    [ResponseCache(Duration = 3600)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var image = await _searchService.GetImageFileAsync(id, cancellationToken);

        return image is null
            ? NotFound()
            : PhysicalFile(image.Path, image.ContentType, enableRangeProcessing: true);
    }
}
