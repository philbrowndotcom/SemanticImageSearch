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
        var people = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        PointId? offset = null;

        do
        {
            var page = await _qdrantClient.ScrollAsync(
                collectionName: _options.CollectionName,
                limit: 1000,
                offset: offset,
                payloadSelector: new WithPayloadSelector
                {
                    Include = new PayloadIncludeSelector { Fields = { "people_names" } }
                },
                vectorsSelector: false,
                cancellationToken: cancellationToken);

            foreach (var point in page.Result)
            {
                if (!point.Payload.TryGetValue("people_names", out var value))
                {
                    continue;
                }

                if (value.ListValue is not null)
                {
                    foreach (var name in value.ListValue.Values.Select(item => item.StringValue))
                    {
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            people.Add(name.Trim());
                        }
                    }
                }
                else
                {
                    foreach (var name in value.StringValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        people.Add(name);
                    }
                }
            }

            offset = page.NextPageOffset;
        }
        while (offset is not null);

        return Ok(people.Order(StringComparer.OrdinalIgnoreCase).ToArray());
    }
}
