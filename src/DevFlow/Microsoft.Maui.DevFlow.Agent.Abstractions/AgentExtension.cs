using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Maui.DevFlow.Extensibility;

namespace Microsoft.Maui.DevFlow.Agent.Core;

public sealed class AgentExtension : IDevFlowExtensionBuilder
{
    private static readonly Regex NamespacePattern = new("^[a-z][a-z0-9]*(\\.[a-z][a-z0-9]*)+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SemanticVersionPattern = new("^\\d+\\.\\d+\\.\\d+(?:-[0-9A-Za-z.-]+)?(?:\\+[0-9A-Za-z.-]+)?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly List<AgentExtensionRoute> _routes = new();
    private readonly List<ExtensionToolDescriptor> _tools = new();
    private readonly List<ExtensionStreamDescriptor> _streams = new();

    public string Namespace { get; }
    public string Description { get; }
    public string Version { get; }
    public IReadOnlyList<string> Features { get; }
    internal IReadOnlyList<AgentExtensionRoute> Routes => _routes;
    internal IReadOnlyList<ExtensionToolDescriptor> Tools => _tools;
    internal IReadOnlyList<ExtensionStreamDescriptor> Streams => _streams;

    /// <summary>Short prefix MCP uses for this extension's tools. Defaults to the last namespace segment.</summary>
    public string? ToolPrefix { get; set; }

    /// <summary><c>ready</c>, or <c>faulted</c> when a plugin failed to configure or start.</summary>
    public string State => Error is null ? "ready" : "faulted";

    /// <summary>Why the plugin faulted, when <see cref="State"/> is <c>faulted</c>.</summary>
    public string? Error { get; internal set; }

    internal DevFlowExtension? Plugin { get; init; }

    public AgentExtension(string @namespace, string description, int version = 1, IEnumerable<string>? features = null)
        : this(@namespace, description, $"{version}.0.0", features)
    {
    }

    public AgentExtension(string @namespace, string description, string version, IEnumerable<string>? features = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(@namespace);

        Namespace = @namespace.Trim();
        if (!NamespacePattern.IsMatch(Namespace))
            throw new ArgumentException("Extension namespace must use reverse-domain notation, e.g. 'com.example.tool'.", nameof(@namespace));

        Description = description?.Trim() ?? string.Empty;
        Version = string.IsNullOrWhiteSpace(version) ? "1.0.0" : version.Trim();
        if (!SemanticVersionPattern.IsMatch(Version))
            throw new ArgumentException("Extension version must be a semantic version, e.g. '1.0.0'.", nameof(version));

        Features = (features ?? Array.Empty<string>())
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public void MapGet(string path, Func<HttpRequest, Task<HttpResponse>> handler)
        => AddRoute("GET", path, handler);

    public void MapPost(string path, Func<HttpRequest, Task<HttpResponse>> handler)
        => AddRoute("POST", path, handler);

    public void MapPut(string path, Func<HttpRequest, Task<HttpResponse>> handler)
        => AddRoute("PUT", path, handler);

    public void MapDelete(string path, Func<HttpRequest, Task<HttpResponse>> handler)
        => AddRoute("DELETE", path, handler);

    public void MapTool(
        string name,
        string description,
        string method,
        string path,
        Func<HttpRequest, Task<HttpResponse>> handler,
        JsonElement? parameters = null,
        JsonElement? returns = null,
        ExtensionToolAnnotations? annotations = null)
        => AddRoute(method, path, handler, name, description, parameters, returns, annotations);

    private void AddRoute(
        string method,
        string path,
        Func<HttpRequest, Task<HttpResponse>> handler,
        string? name = null,
        string? description = null,
        JsonElement? parameters = null,
        JsonElement? returns = null,
        ExtensionToolAnnotations? annotations = null)
    {
        ArgumentNullException.ThrowIfNull(handler);

        var normalizedMethod = method.Trim().ToUpperInvariant();
        var normalizedPath = NormalizePath(path);
        var fullPath = $"/api/v1/ext/{Namespace}{normalizedPath}";
        var toolName = string.IsNullOrWhiteSpace(name) ? BuildToolName(normalizedMethod, normalizedPath) : name.Trim();
        if (_tools.Any(tool => string.Equals(tool.Name, toolName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Duplicate extension tool name registration: {toolName}");

        var requiresMutationLease =
            !normalizedMethod.Equals("GET", StringComparison.OrdinalIgnoreCase) &&
            annotations?.ReadOnly != true;
        _routes.Add(new AgentExtensionRoute(normalizedMethod, fullPath, handler, requiresMutationLease));
        _tools.Add(new ExtensionToolDescriptor
        {
            Name = toolName,
            Description = string.IsNullOrWhiteSpace(description) ? $"{normalizedMethod} {fullPath}" : description.Trim(),
            Method = normalizedMethod,
            Path = fullPath,
            Parameters = parameters,
            Returns = returns,
            Annotations = annotations
        });
    }

    void IDevFlowExtensionBuilder.MapGet(string path, Func<DevFlowRequest, Task<DevFlowResponse>> handler)
        => AddRoute("GET", path, Adapt(handler));

    void IDevFlowExtensionBuilder.MapPost(string path, Func<DevFlowRequest, Task<DevFlowResponse>> handler)
        => AddRoute("POST", path, Adapt(handler));

    void IDevFlowExtensionBuilder.MapPut(string path, Func<DevFlowRequest, Task<DevFlowResponse>> handler)
        => AddRoute("PUT", path, Adapt(handler));

    void IDevFlowExtensionBuilder.MapDelete(string path, Func<DevFlowRequest, Task<DevFlowResponse>> handler)
        => AddRoute("DELETE", path, Adapt(handler));

    void IDevFlowExtensionBuilder.MapTool(
        string name,
        string description,
        string method,
        string path,
        Func<DevFlowRequest, Task<DevFlowResponse>> handler,
        JsonElement? parameters,
        JsonElement? returns,
        DevFlowToolAnnotations? annotations)
        => AddRoute(method, path, Adapt(handler), name, description, parameters, returns, annotations is null ? null : new ExtensionToolAnnotations
        {
            ReadOnly = annotations.ReadOnly,
            Idempotent = annotations.Idempotent,
            Destructive = annotations.Destructive,
            Category = annotations.Category
        });

    /// <summary>
    /// Publishes an event stream at <c>/ws/v1/ext/{namespace}/streams/{name}</c> (live) and
    /// <c>/api/v1/ext/{namespace}/streams/{name}</c> (polling, also exposed as a read-only tool).
    /// </summary>
    public void MapStream(string name, string description, DevFlowEventStream stream, IEnumerable<string>? columns = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(stream);

        var normalizedName = name.Trim();
        if (!StreamNamePattern.IsMatch(normalizedName))
            throw new ArgumentException("Stream names may contain lowercase letters, digits, '-' and '_', e.g. 'events'.", nameof(name));
        if (_streams.Any(s => string.Equals(s.Name, normalizedName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Duplicate extension stream registration: {normalizedName}");

        var relativePath = $"/streams/{normalizedName}";
        var descriptor = new ExtensionStreamDescriptor
        {
            Name = normalizedName,
            Description = description?.Trim() ?? string.Empty,
            Path = $"/api/v1/ext/{Namespace}{relativePath}",
            WebSocketPath = $"/ws/v1/ext/{Namespace}{relativePath}",
            Columns = columns?.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).ToArray() ?? [],
            Stream = stream
        };
        _streams.Add(descriptor);

        AddRoute(
            "GET",
            relativePath,
            request => Task.FromResult(ReadStream(stream, request)),
            StreamToolName(normalizedName),
            $"Read events from the '{normalizedName}' stream: {descriptor.Description} Pass the returned nextSequence as 'since' to continue where the last read stopped.",
            StreamParametersSchema,
            null,
            new ExtensionToolAnnotations { ReadOnly = true, Idempotent = true, Category = "stream" });
    }

    /// <summary>Tool name for polling a stream: <c>events</c> for a stream named "events", else <c>{name}_events</c>.</summary>
    internal static string StreamToolName(string streamName)
        => streamName.EndsWith("events", StringComparison.OrdinalIgnoreCase) ? streamName : $"{streamName}_events";

    internal static HttpResponse ReadStream(DevFlowEventStream stream, HttpRequest request)
    {
        var since = request.QueryParams.TryGetValue("since", out var sinceText) && long.TryParse(sinceText, out var s) ? s : 0;
        var limit = request.QueryParams.TryGetValue("limit", out var limitText) && int.TryParse(limitText, out var l) ? Math.Clamp(l, 1, 1000) : 100;
        var page = stream.Read(since, limit);
        return new HttpResponse
        {
            Body = JsonSerializer.Serialize(new
            {
                events = page.Events.Select(ToWire),
                nextSequence = page.NextSequence,
                dropped = page.Dropped
            }, DevFlowJson.Options)
        };
    }

    internal static object ToWire(DevFlowEvent evt) => new
    {
        seq = evt.Sequence,
        ts = evt.Timestamp.ToString("O"),
        data = evt.Data
    };

    private Func<HttpRequest, Task<HttpResponse>> Adapt(Func<DevFlowRequest, Task<DevFlowResponse>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return async request =>
        {
            if (Error is not null)
                return HttpResponse.Error($"Extension '{Namespace}' is faulted: {Error}", 503, "extension-faulted");

            DevFlowResponse response;
            try
            {
                response = await handler(new DevFlowRequest(
                    request.Method,
                    request.Path,
                    request.QueryParams,
                    request.RouteParams,
                    request.Headers,
                    request.Body,
                    request.CancellationToken)).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (request.CancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                return HttpResponse.Error($"Extension '{Namespace}' failed: {ex.Message}", 500, "extension-error");
            }

            return new HttpResponse
            {
                StatusCode = response.StatusCode,
                StatusText = HttpResponse.StatusTextFor(response.StatusCode),
                ContentType = response.ContentType,
                Body = response.Body,
                BodyBytes = response.BodyBytes
            };
        };
    }

    private static readonly Regex StreamNamePattern = new("^[a-z0-9][a-z0-9_-]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly JsonElement StreamParametersSchema = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "since": { "type": "integer", "description": "Return events after this sequence number. 0 reads the whole buffer." },
            "limit": { "type": "integer", "description": "Maximum events to return (1-1000, default 100)." }
          }
        }
        """).RootElement.Clone();

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "/")
            return string.Empty;

        var trimmed = path.Trim();
        return trimmed.StartsWith('/') ? trimmed : $"/{trimmed}";
    }

    private static string BuildToolName(string method, string path)
    {
        var source = string.IsNullOrWhiteSpace(path) || path == "/" ? "root" : path.Trim('/');
        var sanitized = Regex.Replace(source.ToLowerInvariant(), "[^a-z0-9]+", "_").Trim('_');
        return string.IsNullOrWhiteSpace(sanitized)
            ? method.ToLowerInvariant()
            : $"{method.ToLowerInvariant()}_{sanitized}";
    }
}

internal sealed class AgentExtensionRoute
{
    public AgentExtensionRoute(
        string method,
        string path,
        Func<HttpRequest, Task<HttpResponse>> handler,
        bool requiresMutationLease)
    {
        Method = method;
        Path = path;
        Handler = handler;
        RequiresMutationLease = requiresMutationLease;
    }

    public string Method { get; }
    public string Path { get; }
    public Func<HttpRequest, Task<HttpResponse>> Handler { get; }
    public bool RequiresMutationLease { get; }
}

public sealed class ExtensionStreamDescriptor
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Path { get; init; }
    public required string WebSocketPath { get; init; }
    public IReadOnlyList<string> Columns { get; init; } = [];
    internal DevFlowEventStream Stream { get; init; } = null!;
}

public sealed class ExtensionToolDescriptor
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Method { get; init; }
    public required string Path { get; init; }
    public JsonElement? Parameters { get; init; }
    public JsonElement? Returns { get; init; }
    public ExtensionToolAnnotations? Annotations { get; init; }
}

public sealed class ExtensionToolAnnotations
{
    public bool ReadOnly { get; init; }
    public bool Idempotent { get; init; }
    public bool Destructive { get; init; }
    public string? Category { get; init; }
}
