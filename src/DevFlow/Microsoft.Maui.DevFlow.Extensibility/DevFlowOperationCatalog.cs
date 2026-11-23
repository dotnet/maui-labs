using System.Text.Json;

namespace Microsoft.Maui.DevFlow.Extensibility;

/// <summary>The explicitly selected target an operation needs.</summary>
public enum DevFlowTargetRequirement { None, Device, Agent }

/// <summary>Target selected by the caller, never by plugin discovery.</summary>
public sealed record DevFlowTarget(string? DeviceId = null, string? AgentHost = null, int? AgentPort = null);

/// <summary>A transport-neutral operation invocation.</summary>
public sealed record DevFlowOperationRequest(
    JsonElement? Parameters = null,
    CancellationToken CancellationToken = default,
    DevFlowTarget? Target = null);

/// <summary>A named operation and its optional compatibility HTTP binding.</summary>
public sealed record DevFlowOperation(
    string Name,
    string Description,
    Func<DevFlowOperationRequest, Task<DevFlowResponse>> Handler,
    JsonElement? Parameters = null,
    JsonElement? Returns = null,
    DevFlowToolAnnotations? Annotations = null,
    DevFlowTargetRequirement Target = DevFlowTargetRequirement.None,
    string? Method = null,
    string? Path = null)
{
    public bool IsReadOnly => Annotations?.ReadOnly == true || string.Equals(Method, "GET", StringComparison.OrdinalIgnoreCase);
}

/// <summary>A stream exposed by either an app-side or a host-side plugin.</summary>
public sealed record DevFlowStream(string Name, string Description, DevFlowEventStream Buffer, IReadOnlyList<string> Columns);

/// <summary>
/// Shared registration and invocation for agent plugins, host plugins and built-ins. Transports bind
/// operation identities to their own routes; plugin handlers need not know about HTTP.
/// </summary>
public sealed class DevFlowOperationCatalog : IDevFlowExtensionBuilder
{
    private readonly Dictionary<string, DevFlowOperation> _operations = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DevFlowStream> _streams = new(StringComparer.OrdinalIgnoreCase);
    private bool _frozen;

    public IReadOnlyCollection<DevFlowOperation> Operations { get { lock (_operations) return _operations.Values.ToArray(); } }
    public IReadOnlyCollection<DevFlowStream> Streams { get { lock (_operations) return _streams.Values.ToArray(); } }

    /// <summary>Ends configuration. Registered handlers and streams remain usable.</summary>
    public void Freeze() { lock (_operations) _frozen = true; }

    public void MapOperation(
        string name,
        string description,
        Func<DevFlowOperationRequest, Task<DevFlowResponse>> handler,
        JsonElement? parameters = null,
        JsonElement? returns = null,
        DevFlowToolAnnotations? annotations = null,
        DevFlowTargetRequirement target = DevFlowTargetRequirement.None)
        => Register(new(name, description, handler, parameters, returns, annotations, target));

    public void Register(DevFlowOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation.Name);
        ArgumentNullException.ThrowIfNull(operation.Handler);
        lock (_operations)
        {
            if (_frozen) throw new InvalidOperationException("The operation catalog is frozen.");
            if (!_operations.TryAdd(operation.Name, operation with { Parameters = operation.Parameters?.Clone(), Returns = operation.Returns?.Clone() }))
                throw new InvalidOperationException($"Duplicate operation name registration: {operation.Name}");
        }
    }

    public Task<DevFlowResponse> InvokeAsync(string name, DevFlowOperationRequest request)
    {
        request.CancellationToken.ThrowIfCancellationRequested();
        DevFlowOperation operation;
        lock (_operations)
            operation = _operations.GetValueOrDefault(name) ?? throw new KeyNotFoundException($"Operation '{name}' is not registered.");
        if (request.Parameters is { ValueKind: not (JsonValueKind.Object or JsonValueKind.Null) })
            throw new ArgumentException("Operation parameters must be a JSON object.");
        if (operation.Target == DevFlowTargetRequirement.Device && string.IsNullOrWhiteSpace(request.Target?.DeviceId))
            throw new ArgumentException("This operation requires an explicitly selected device.");
        if (operation.Target == DevFlowTargetRequirement.Agent &&
            (string.IsNullOrWhiteSpace(request.Target?.AgentHost) || request.Target?.AgentPort is not (> 0 and <= 65535)))
            throw new ArgumentException("This operation requires an explicitly selected agent.");
        return operation.Handler(request);
    }

    public void MapGet(string path, Func<DevFlowRequest, Task<DevFlowResponse>> handler) => MapRoute("GET", path, handler);
    public void MapPost(string path, Func<DevFlowRequest, Task<DevFlowResponse>> handler) => MapRoute("POST", path, handler);
    public void MapPut(string path, Func<DevFlowRequest, Task<DevFlowResponse>> handler) => MapRoute("PUT", path, handler);
    public void MapDelete(string path, Func<DevFlowRequest, Task<DevFlowResponse>> handler) => MapRoute("DELETE", path, handler);

    private void MapRoute(string method, string path, Func<DevFlowRequest, Task<DevFlowResponse>> handler)
    {
        ArgumentNullException.ThrowIfNull(path);
        var source = path.Trim('/').ToLowerInvariant();
        var name = new string(source.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_').ToArray()).Trim('_');
        MapTool($"{method.ToLowerInvariant()}_{(name.Length == 0 ? "root" : name)}", $"{method} {path}", method, path, handler);
    }

    public void MapTool(
        string name, string description, string method, string path,
        Func<DevFlowRequest, Task<DevFlowResponse>> handler,
        JsonElement? parameters = null, JsonElement? returns = null, DevFlowToolAnnotations? annotations = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        var normalizedMethod = method.Trim().ToUpperInvariant();
        if (normalizedMethod is not ("GET" or "POST" or "PUT" or "DELETE"))
            throw new ArgumentException($"Unsupported operation HTTP method: {method}");
        Register(new(name, description, request =>
        {
            var query = new Dictionary<string, string>();
            var boundPath = ExtensionRouteBinding.Bind(path, request.Parameters, out var route);
            if (normalizedMethod == "GET" && request.Parameters is { ValueKind: JsonValueKind.Object } values)
                foreach (var property in values.EnumerateObject())
                    if (!route.ContainsKey(property.Name))
                        query[property.Name] = property.Value.ValueKind == JsonValueKind.String
                            ? property.Value.GetString()! : property.Value.GetRawText();
            return handler(new DevFlowRequest(normalizedMethod, boundPath, query, route,
                body: normalizedMethod == "GET" ? null : request.Parameters?.GetRawText(),
                cancellationToken: request.CancellationToken));
        }, parameters, returns, annotations, Method: normalizedMethod, Path: path));
    }

    public void MapStream(string name, string description, DevFlowEventStream stream, IEnumerable<string>? columns = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(stream);
        lock (_operations)
        {
            if (_streams.ContainsKey(name))
                throw new InvalidOperationException($"Duplicate stream name registration: {name}");
            MapOperation(name.EndsWith("events", StringComparison.OrdinalIgnoreCase) ? name : $"{name}_events",
                $"Read events from '{name}'. Pass nextSequence as since to continue. {description}", request =>
                {
                    var parameters = request.Parameters;
                    var since = parameters is { ValueKind: JsonValueKind.Object } p && p.TryGetProperty("since", out var s) ? s.GetInt64() : 0;
                    var limit = parameters is { ValueKind: JsonValueKind.Object } q && q.TryGetProperty("limit", out var l) ? Math.Clamp(l.GetInt32(), 1, 1000) : 100;
                    return Task.FromResult(DevFlowResponse.Json(StreamPage(stream.Read(since, limit))));
                }, StreamParameters, annotations: new() { ReadOnly = true, Idempotent = true, Category = "stream" });
            _streams.Add(name, new(name, description, stream, columns?.ToArray() ?? []));
        }
    }

    public static object StreamPage(DevFlowEventPage page) => new Dictionary<string, object?>
    {
        ["events"] = page.Events.Select(e => (object)new Dictionary<string, object?>
        {
            ["seq"] = e.Sequence, ["ts"] = e.Timestamp.ToString("O"), ["data"] = e.Data
        }).ToArray(),
        ["nextSequence"] = page.NextSequence,
        ["dropped"] = page.Dropped
    };

    internal static readonly JsonElement StreamParameters = JsonDocument.Parse("""
        {"type":"object","properties":{"since":{"type":"integer"},"limit":{"type":"integer","minimum":1,"maximum":1000}}}
        """).RootElement.Clone();
}

/// <summary>
/// A plugin explicitly installed in the developer-machine host. It cannot be activated by an app's
/// capabilities response. Use MapOperation and the invocation's selected Target for device work.
/// </summary>
public abstract class DevFlowHostExtension(string @namespace, string description, string version = "1.0.0")
    : DevFlowExtension(@namespace, description, version);
