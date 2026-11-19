namespace Microsoft.Maui.DevFlow.Extensibility;

/// <summary>Transport names granted by the application or tooling host, never by plugin metadata.</summary>
public sealed class DevFlowProviderBindings
{
    public string? HttpRoutePrefix { get; }
    public string? WebSocketRoutePrefix { get; }
    public string? McpToolPrefix { get; }

    public DevFlowProviderBindings(string? httpRoutePrefix = null, string? webSocketRoutePrefix = null, string? mcpToolPrefix = null)
    {
        HttpRoutePrefix = ValidatePrefix(httpRoutePrefix, "/api/v1/");
        WebSocketRoutePrefix = ValidatePrefix(webSocketRoutePrefix, "/ws/v1/");
        if (mcpToolPrefix is not null &&
            (mcpToolPrefix.Length is 0 or > 48 || !char.IsAsciiLetterLower(mcpToolPrefix[0]) ||
             mcpToolPrefix.Any(c => !char.IsAsciiLetterLower(c) && !char.IsAsciiDigit(c) && c != '_')))
            throw new ArgumentException("MCP prefixes must start with a lowercase letter and contain only lowercase letters, digits, or underscores (maximum 48 characters).", nameof(mcpToolPrefix));
        McpToolPrefix = mcpToolPrefix;
    }

    private static string? ValidatePrefix(string? prefix, string root)
    {
        if (prefix is null) return null;
        if (!prefix.StartsWith(root, StringComparison.Ordinal) || prefix.TrimEnd('/').Length <= root.TrimEnd('/').Length ||
            !DevFlowProviderPolicy.IsCanonicalPath(prefix) ||
            prefix.Contains('%') || prefix.Contains('{') || prefix.Contains('}'))
            throw new ArgumentException($"A provider route prefix must be a canonical path below {root}.", nameof(prefix));
        return prefix.TrimEnd('/');
    }
}

/// <summary>
/// Explicit allowlist for non-extension transport bindings. Configure before registering providers.
/// These grants do not change execution location, mutation leases, or plugin trust.
/// </summary>
public sealed class DevFlowProviderPolicy
{
    private static readonly Dictionary<string, DevFlowProviderBindings> BuiltIns = new(StringComparer.Ordinal)
    {
        ["devflow.agent"] = new("/api/v1/agent", mcpToolPrefix: "maui_agent"),
        ["devflow.actions"] = new("/api/v1/invoke/actions", mcpToolPrefix: "maui_action")
    };
    private readonly Dictionary<string, DevFlowProviderBindings> _bindings = new(BuiltIns, StringComparer.Ordinal);
    private bool _frozen;

    internal static bool IsBuiltIn(string provider) => BuiltIns.ContainsKey(provider);

    public DevFlowProviderPolicy Allow(string provider, DevFlowProviderBindings bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        var ns = provider.StartsWith("host:", StringComparison.Ordinal) ? provider[5..] : provider;
        DevFlowExtension.ValidateIdentity(ns, "1.0.0");
        if (IsBuiltIn(provider))
            throw new ArgumentException("The built-in provider's bindings cannot be replaced.", nameof(provider));
        lock (_bindings)
        {
            if (_frozen) throw new InvalidOperationException("Provider bindings are frozen; configure grants before registering providers.");
            if (!_bindings.TryAdd(provider, bindings))
                throw new InvalidOperationException($"Bindings for provider '{provider}' are already registered.");
        }
        return this;
    }

    public void Freeze() { lock (_bindings) _frozen = true; }

    public DevFlowProviderBindings? GetBindings(string provider)
    {
        lock (_bindings) return _bindings.GetValueOrDefault(provider);
    }

    public bool AllowsPath(string provider, string path, bool webSocket = false)
    {
        if (provider.StartsWith("host:", StringComparison.Ordinal) || !IsCanonicalPath(path)) return false;
        var bindings = GetBindings(provider);
        var prefix = (webSocket ? bindings?.WebSocketRoutePrefix : bindings?.HttpRoutePrefix)
            ?? $"{(webSocket ? "/ws" : "/api")}/v1/ext/{provider}";
        return path == prefix || path.StartsWith(prefix + "/", StringComparison.Ordinal);
    }

    internal static bool IsCanonicalPath(string path)
    {
        if (string.IsNullOrEmpty(path) || path[0] != '/' || path.Contains("//", StringComparison.Ordinal) ||
            path.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) || c is '\\' or '?' or '#'))
            return false;
        return path.Split('/').All(segment =>
        {
            var decoded = Uri.UnescapeDataString(segment);
            return !decoded.Contains('\\') && decoded.Split('/').All(part => part is not ("." or ".."));
        });
    }
}
