using Qdrant.Client.Grpc;

public static class PeoplePayload
{
    public const string Key = "people_names";

    // Names are returned exactly as stored so they can be used as-is in the keyword filter.
    public static string[] Read(IDictionary<string, Value> payload) =>
        payload.TryGetValue(Key, out var value) && value.ListValue is not null
            ? value.ListValue.Values
                .Select(item => item.StringValue)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToArray()
            : [];
}
