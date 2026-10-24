using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using SemanticImageSearch.Core.Options;

[ApiController]
[Route("api/[controller]")]
public class PeopleController : ControllerBase
{
    private readonly QdrantClient _qdrantClient;
    private readonly QdrantOptions _options;

    public PeopleController(QdrantClient qdrantClient, IOptions<QdrantOptions> options)
    {
        _qdrantClient = qdrantClient;
        _options = options.Value;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<string>>> Get(CancellationToken cancellationToken)
    {
        // Case-sensitive so every listed name matches the exact keyword filter used by search.
        var people = new HashSet<string>(StringComparer.Ordinal);
        PointId? offset = null;

        do
        {
            var page = await _qdrantClient.ScrollAsync(
                collectionName: _options.CollectionName,
                limit: 1000,
                offset: offset,
                payloadSelector: new WithPayloadSelector
                {
                    Include = new PayloadIncludeSelector { Fields = { PeoplePayload.Key } }
                },
                vectorsSelector: false,
                cancellationToken: cancellationToken);

            foreach (var point in page.Result)
            {
                people.UnionWith(PeoplePayload.Read(point.Payload));
            }

            offset = page.NextPageOffset;
        }
        while (offset is not null);

        return Ok(people.Order(StringComparer.OrdinalIgnoreCase).ToArray());
    }
}
