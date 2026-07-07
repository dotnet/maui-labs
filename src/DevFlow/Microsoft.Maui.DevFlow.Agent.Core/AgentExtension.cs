using System.Text.Json;
using System.Text.RegularExpressions;

namespace Microsoft.Maui.DevFlow.Agent.Core;

public sealed class AgentExtension
{
    private static readonly Regex NamespacePattern = new("^[a-z][a-z0-9]*(\\.[a-z][a-z0-9]*)+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SemanticVersionPattern = new("^\\d+\\.\\d+\\.\\d+(?:-[0-9A-Za-z.-]+)?(?:\\+[0-9A-Za-z.-]+)?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly List<AgentExtensionRoute> _routes = new();
    private readonly List<ExtensionToolDescriptor> _tools = new();

    public string Namespace { get; }
    public string Description { get; }
    public string Version { get; }
    public IReadOnlyList<string> Features { get; }
    internal IReadOnlyList<AgentExtensionRoute> Routes => _routes;
    internal IReadOnlyList<ExtensionToolDescriptor> Tools => _tools;

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

        _routes.Add(new AgentExtensionRoute(normalizedMethod, fullPath, handler));
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
    public AgentExtensionRoute(string method, string path, Func<HttpRequest, Task<HttpResponse>> handler)
    {
        Method = method;
        Path = path;
        Handler = handler;
    }

    public string Method { get; }
    public string Path { get; }
    public Func<HttpRequest, Task<HttpResponse>> Handler { get; }
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
