namespace Microsoft.Maui.Cli.DevFlow.Inspector;

/// <summary>
/// Origin-header validation for localhost-only HTTP/WebSocket endpoints.
/// Used to defend against cross-origin attacks (CSRF on POST endpoints, hijacked
/// WebSocket subscriptions) when the only legitimate callers are the DevFlow CLI
/// (no Origin header) or a browser session on the same loopback origin.
/// </summary>
internal static class LocalOriginValidator
{
    /// <summary>
    /// Returns true if the origin is either absent (non-browser client) or a
    /// loopback HTTP/HTTPS URI (http://localhost*, http://127.0.0.1*, http://[::1]*).
    /// </summary>
    public static bool IsAllowed(string? origin)
    {
        // No Origin header: non-browser client (e.g. CLI tool, curl). Permit.
        if (string.IsNullOrEmpty(origin) || origin == "null")
            return true;

        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
            return false;

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return false;

        var host = uri.Host;
        return host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host == "127.0.0.1"
            || host == "[::1]"
            || host == "::1";
    }
}
