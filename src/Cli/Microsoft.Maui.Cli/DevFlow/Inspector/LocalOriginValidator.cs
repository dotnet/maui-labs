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
    /// The literal Origin "null" — sent by browsers for file:// pages, sandboxed
    /// iframes, data: URLs, and other opaque origins — is rejected.
    /// </summary>
    public static bool IsAllowed(string? origin) => IsAllowed(origin, expectedPort: 0);

    /// <summary>
    /// Returns true if the origin is either absent or a loopback HTTP/HTTPS URI
    /// matching <paramref name="expectedPort"/>. Per RFC 6454, an origin is
    /// scheme+host+port — a page on port 3000 is a distinct security principal
    /// from one on port 9000 even if both are loopback. Passing
    /// <paramref name="expectedPort"/> = 0 disables the port check (loopback host
    /// only). Otherwise the origin's port must equal <paramref name="expectedPort"/>.
    /// This stops a malicious or compromised page on any other loopback port
    /// (e.g. a dev server) from issuing CSRF POSTs to the broker / inspector.
    /// </summary>
    public static bool IsAllowed(string? origin, int expectedPort)
    {
        // No Origin header at all: non-browser client (e.g. CLI tool, curl). Permit.
        if (string.IsNullOrEmpty(origin))
            return true;

        // Browsers send literal "null" for file:// pages, sandboxed iframes, data:
        // URLs, and other opaque origins. These are not loopback — treat as foreign.
        if (origin == "null")
            return false;

        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
            return false;

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return false;

        var host = uri.Host;
        var hostOk = host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host == "127.0.0.1"
            || host == "[::1]"
            || host == "::1";
        if (!hostOk)
            return false;

        // Port enforcement: only when caller supplied a non-zero expected port.
        // uri.Port is the effective port (defaults to 80/443 if omitted from origin).
        if (expectedPort > 0 && uri.Port != expectedPort)
            return false;

        return true;
    }
}
