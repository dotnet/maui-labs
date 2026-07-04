using System.Net;
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
        request.Headers.Accept.ParseAdd("application/json");

        if (args is not null && args.TryGetPropertyValue(BodyKey, out var body) && body is not null)
        {
            var json = body.ToJsonString();
            if (Encoding.UTF8.GetByteCount(json) > _options.MaxRequestBytes)
                throw new InvalidOperationException($"Request body exceeds the {_options.MaxRequestBytes}-byte cap.");
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return request;
    }

    /// <summary>
    /// Resolves <paramref name="operationId"/>, enforces that its HTTP method matches
    /// <paramref name="access"/> (read ⇒ GET, write ⇒ mutating), sends the request, and returns a
    /// normalized <see cref="ApiResult"/> — a capped JSON body on success or a structured error on
    /// failure. Model-correctable problems (unknown operation, wrong tool, missing argument, server
    /// 4xx/5xx) are returned as errors, never thrown.
    /// </summary>
    public async Task<ApiResult> InvokeAsync(
        ReducedSpec spec,
        string operationId,
        JsonObject? args = null,
        ApiAccess access = ApiAccess.Read,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var endpoint = spec.Endpoints.FirstOrDefault(e => string.Equals(e.OperationId, operationId, StringComparison.Ordinal));
        if (endpoint is null)
            return ApiResult.Fail(0, "no_such_operation", $"No operation with id '{operationId}'.");

        var isRead = string.Equals(endpoint.Method, "GET", StringComparison.Ordinal);
        if (access == ApiAccess.Read && !isRead)
            return ApiResult.Fail(0, "wrong_tool", $"Operation '{operationId}' is a {endpoint.Method}; use write_api.");
        if (access == ApiAccess.Write && isRead)
            return ApiResult.Fail(0, "wrong_tool", $"Operation '{operationId}' is a read (GET); use read_api.");

        HttpRequestMessage request;
        try
        {
            request = BuildRequest(spec, operationId, args);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return ApiResult.Fail(0, "bad_request", ex.Message);
        }

        using (request)
        using (var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false))
        {
            return await NormalizeAsync(response, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<ApiResult> NormalizeAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var status = (int)response.StatusCode;
        var raw = response.Content is null
            ? string.Empty
            : await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            if (string.IsNullOrEmpty(raw))
                return ApiResult.Ok(status, null, truncated: false);

            var (body, truncated) = Cap(raw);
            return ApiResult.Ok(status, body, truncated);
        }

        var (title, detail) = ParseProblem(raw, response);
        return ApiResult.Fail(status, title, detail);
    }

    private (string Body, bool Truncated) Cap(string raw)
    {
        if (Encoding.UTF8.GetByteCount(raw) <= _options.MaxResponseBytes)
            return (raw, false);

        var builder = new StringBuilder();
        var used = 0;
        foreach (var rune in raw.EnumerateRunes())
        {
            var runeBytes = rune.Utf8SequenceLength;
            if (used + runeBytes > _options.MaxResponseBytes)
                break;
            builder.Append(rune.ToString());
            used += runeBytes;
        }

        return (builder.ToString(), true);
    }

    private static (string Title, string? Detail) ParseProblem(string raw, HttpResponseMessage response)
    {
        var fallbackTitle = response.ReasonPhrase ?? $"HTTP {(int)response.StatusCode}";

        if (!string.IsNullOrWhiteSpace(raw))
        {
            try
            {
                if (JsonNode.Parse(raw) is JsonObject problem)
                {
                    var title = AsString(problem["title"]);
                    var detail = AsString(problem["detail"]);
                    if (title is not null || detail is not null)
                        return (title ?? fallbackTitle, detail);
                }
            }
            catch (System.Text.Json.JsonException)
            {
                // Not JSON; fall through to returning the raw text as the detail.
            }
        }

        return (fallbackTitle, string.IsNullOrWhiteSpace(raw) ? null : raw);
    }

    private static string? AsString(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var s) ? s : null;

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
