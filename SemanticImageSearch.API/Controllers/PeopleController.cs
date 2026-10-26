using Microsoft.AspNetCore.Mvc;
using SemanticImageSearch.Core.Interfaces;

[ApiController]
[Route("api/[controller]")]
public class PeopleController : ControllerBase
{
    private readonly IImageSearchService _searchService;

    public PeopleController(IImageSearchService searchService)
    {
        _searchService = searchService;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<string>>> Get(CancellationToken cancellationToken) =>
        Ok(await _searchService.GetPeopleAsync(cancellationToken));
}
