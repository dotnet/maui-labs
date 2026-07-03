namespace Microsoft.Maui.AI.GenerativeUI.OpenApi;

/// <summary>
/// How the OpenAPI document is fetched.
/// </summary>
public enum SpecFetchMode
{
    /// <summary>Fetch and reduce the spec in the background at startup.</summary>
    Eager,

    /// <summary>Fetch and reduce the spec on first server-API use.</summary>
    Lazy,
}

/// <summary>
/// App-owned configuration for the OpenAPI side of Generative UI. The model never sees any of this.
/// </summary>
public sealed class GenerativeOpenApiOptions
{
    /// <summary>Server root. Every call is resolved relative to it. Required.</summary>
    public Uri? BaseAddress { get; set; }

    /// <summary>Path the OpenAPI document is fetched from. Defaults to <c>/openapi/v1.json</c>.</summary>
    public string OpenApiPath { get; set; } = "/openapi/v1.json";

    /// <summary>
    /// Callback to configure the invoker's <see cref="HttpClient"/> (headers, timeouts, auth).
    /// Credentials live here — never in the model.
    /// </summary>
    public Action<HttpClient>? ConfigureHttpClient { get; set; }

    /// <summary>
    /// SSRF allowlist of hosts the invoker may call. When null or empty, only the
    /// <see cref="BaseAddress"/> host is allowed.
    /// </summary>
    public IReadOnlyList<string>? AllowedHosts { get; set; }

    /// <summary>When the spec is fetched. Defaults to <see cref="SpecFetchMode.Eager"/>.</summary>
    public SpecFetchMode SpecFetch { get; set; } = SpecFetchMode.Eager;

    /// <summary>Whether to seed the compact endpoint index into the system prompt. Defaults to true.</summary>
    public bool SeedEndpointIndex { get; set; } = true;

    /// <summary>Cap on a response body fed back to the model, in bytes. Defaults to 64 KB.</summary>
    public int MaxResponseBytes { get; set; } = 64 * 1024;

    /// <summary>Cap on a serialized request body, in bytes. Defaults to 1 MB.</summary>
    public int MaxRequestBytes { get; set; } = 1024 * 1024;

    /// <summary><c>$ref</c> expansion depth before falling back to a name reference. Defaults to 5.</summary>
    public int RefResolutionDepth { get; set; } = 5;
}
