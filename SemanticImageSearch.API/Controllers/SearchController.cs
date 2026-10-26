using Microsoft.AspNetCore.Mvc;
using SemanticImageSearch.Core.Exceptions;
using SemanticImageSearch.Core.Interfaces;
using SemanticImageSearch.Core.Models;

[ApiController]
[Route("api/[controller]")]
public class SearchController : ControllerBase
{
    private readonly IImageSearchService _searchService;

    public SearchController(IImageSearchService searchService)
    {
        _searchService = searchService;
    }

    [HttpGet]
    public async Task<IActionResult> Search(
        [FromQuery] string? query,
        [FromQuery] int limit = 10,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] string[]? people = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return Ok(await _searchService.SearchAsync(new SearchRequest(query, limit, from, to, people), cancellationToken));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception ex) when (ex is not ServiceUnavailableException)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
