using System.Text;
using System.Text.Json.Nodes;

namespace Microsoft.Maui.AI.GenerativeUI.OpenApi;

/// <summary>
/// Executes an operation from a <see cref="ReducedSpec"/> generically. The caller identifies the
/// operation by <c>operationId</c> and supplies a flat <c>args</c> object: path and query values as
/// top-level keys, and the request payload under an explicit <c>body</c> key. The invoker routes each
/// value using the operation's parameter metadata, so path/query keys can never be confused with body
/// fields, and the HTTP method comes from the operation (a read can never become a write).
/// </summary>
public sealed class ApiInvoker
{
    private const string BodyKey = "body";

    private readonly GenerativeOpenApiOptions _options;
    private readonly HttpClient _httpClient;

    public ApiInvoker(GenerativeOpenApiOptions options, HttpClient? httpClient = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (_options.BaseAddress is null)
            throw new ArgumentException($"{nameof(GenerativeOpenApiOptions.BaseAddress)} is required.", nameof(options));

        _httpClient = httpClient ?? new HttpClient();
        _options.ConfigureHttpClient?.Invoke(_httpClient);
    }

    /// <summary>
    /// Builds (but does not send) the HTTP request for <paramref name="operationId"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// No such operation, a required path argument is missing, or the resolved host is not allowed.
    /// </exception>
    public HttpRequestMessage BuildRequest(ReducedSpec spec, string operationId, JsonObject? args = null)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var endpoint = spec.Endpoints.FirstOrDefault(e => string.Equals(e.OperationId, operationId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"No operation with id '{operationId}'.");

        var path = endpoint.Path;
        var query = new List<string>();

        foreach (var parameter in endpoint.Parameters ?? [])
        {
            var value = TryGetArg(args, parameter.Name);
            if (value is null)
            {
                if (parameter.Required && parameter.In == "path")
                    throw new InvalidOperationException($"Missing required arg '{parameter.Name}' for operation '{operationId}'.");
                continue;
            }

            switch (parameter.In)
            {
                case "path":
                    path = path.Replace("{" + parameter.Name + "}", Uri.EscapeDataString(value));
                    break;
                case "query":
                    query.Add($"{Uri.EscapeDataString(parameter.Name)}={Uri.EscapeDataString(value)}");
                    break;
                // header/cookie parameters are out of scope for the MVP.
            }
        }

        var relative = query.Count > 0 ? $"{path}?{string.Join('&', query)}" : path;
        var uri = new Uri(_options.BaseAddress!, relative.TrimStart('/'));
        EnsureAllowedHost(uri);

        var request = new HttpRequestMessage(new HttpMethod(endpoint.Method), uri);

        if (args is not null && args.TryGetPropertyValue(BodyKey, out var body) && body is not null)
        {
            var json = body.ToJsonString();
            if (Encoding.UTF8.GetByteCount(json) > _options.MaxRequestBytes)
                throw new InvalidOperationException($"Request body exceeds the {_options.MaxRequestBytes}-byte cap.");
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return request;
    }

    /// <summary>Builds and sends the request for <paramref name="operationId"/>.</summary>
    public Task<HttpResponseMessage> SendAsync(ReducedSpec spec, string operationId, JsonObject? args = null, CancellationToken cancellationToken = default)
        => _httpClient.SendAsync(BuildRequest(spec, operationId, args), cancellationToken);

    private static string? TryGetArg(JsonObject? args, string name)
    {
        if (args is null || !args.TryGetPropertyValue(name, out var node) || node is null)
            return null;

        if (node is JsonValue value)
            return value.TryGetValue<string>(out var s) ? s : value.ToJsonString();

        return node.ToJsonString();
    }

    private void EnsureAllowedHost(Uri uri)
    {
        var allowed = _options.AllowedHosts is { Count: > 0 }
            ? _options.AllowedHosts
            : [_options.BaseAddress!.Host];

        if (!allowed.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Host '{uri.Host}' is not in the allowlist.");
    }
}
