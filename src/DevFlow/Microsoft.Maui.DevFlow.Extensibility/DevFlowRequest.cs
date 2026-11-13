using System.Text.Json;

namespace Microsoft.Maui.DevFlow.Extensibility;

/// <summary>A call to a plugin route or tool.</summary>
public sealed class DevFlowRequest
{
    JsonElement? bodyJson;
    bool bodyParsed;

    /// <summary>Creates a request. The agent builds these; tests can too.</summary>
    public DevFlowRequest(
        string method,
        string path,
        IReadOnlyDictionary<string, string>? query = null,
        IReadOnlyDictionary<string, string>? routeValues = null,
        IReadOnlyDictionary<string, string>? headers = null,
        string? body = null,
        CancellationToken cancellationToken = default)
    {
        Method = method;
        Path = path;
        Query = query ?? new Dictionary<string, string>();
        RouteValues = routeValues ?? new Dictionary<string, string>();
        Headers = headers ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Body = body;
        CancellationToken = cancellationToken;
    }

    /// <summary>HTTP method in upper case.</summary>
    public string Method { get; }

    /// <summary>Full request path, e.g. <c>/api/v1/ext/org.shiny.ble/status</c>.</summary>
    public string Path { get; }

    /// <summary>Query string values.</summary>
    public IReadOnlyDictionary<string, string> Query { get; }

    /// <summary>Values captured by <c>{name}</c> segments in the route path.</summary>
    public IReadOnlyDictionary<string, string> RouteValues { get; }

    /// <summary>Request headers.</summary>
    public IReadOnlyDictionary<string, string> Headers { get; }

    /// <summary>Raw request body, or null for GET.</summary>
    public string? Body { get; }

    /// <summary>Cancelled when the caller disconnects or the agent stops.</summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>The body parsed as JSON, or null when it is empty or not JSON.</summary>
    public JsonElement? BodyJson
    {
        get
        {
            if (!bodyParsed)
            {
                bodyParsed = true;
                if (!string.IsNullOrWhiteSpace(Body))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(Body);
                        bodyJson = doc.RootElement.Clone();
                    }
                    catch (JsonException)
                    {
                        bodyJson = null;
                    }
                }
            }
            return bodyJson;
        }
    }

    /// <summary>Deserializes the body with web defaults (camelCase, case-insensitive).</summary>
    public T? ReadJson<T>()
        => string.IsNullOrWhiteSpace(Body) ? default : JsonSerializer.Deserialize<T>(Body, DevFlowJson.Options);

    /// <summary>
    /// Returns a tool parameter by name, looking in route values, then the query string, then a
    /// top-level property of a JSON object body. This lets one handler serve GET and POST callers.
    /// </summary>
    public string? GetParameter(string name)
    {
        if (RouteValues.TryGetValue(name, out var route))
            return route;
        if (Query.TryGetValue(name, out var query))
            return query;
        if (BodyJson is { ValueKind: JsonValueKind.Object } body)
        {
            foreach (var property in body.EnumerateObject())
            {
                if (!property.NameEquals(name) && !string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                    continue;

                return property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString(),
                    JsonValueKind.Null => null,
                    _ => property.Value.GetRawText()
                };
            }
        }
        return null;
    }
}
