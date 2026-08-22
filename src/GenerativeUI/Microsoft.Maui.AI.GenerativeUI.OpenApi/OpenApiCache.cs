namespace Microsoft.Maui.AI.GenerativeUI.OpenApi;

/// <summary>
/// Fetches the server's OpenAPI document and reduces it into a <see cref="ReducedSpec"/> once, caching
/// the result for the process lifetime. Thread-safe: concurrent callers await a single fetch. Intended
/// to be registered as a singleton.
/// </summary>
public sealed class OpenApiCache
{
    private readonly GenerativeOpenApiOptions? _options;
    private readonly HttpClient? _httpClient;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ReducedSpec? _spec;

    /// <summary>Creates a cache that lazily fetches and reduces the spec on first use.</summary>
    public OpenApiCache(GenerativeOpenApiOptions options, HttpClient httpClient)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    private OpenApiCache(ReducedSpec spec) => _spec = spec;

    /// <summary>
    /// Creates a pre-loaded cache around an already-reduced spec (useful for tests and callers that
    /// have the spec in hand). <see cref="GetSpecAsync"/> returns it without any network access.
    /// </summary>
    public static OpenApiCache FromSpec(ReducedSpec spec) => new(spec ?? throw new ArgumentNullException(nameof(spec)));

    /// <summary>The reduced spec if it has already been loaded; otherwise null.</summary>
    public ReducedSpec? Current => _spec;

    /// <summary>
    /// Returns the reduced spec, fetching and reducing the OpenAPI document on first call. Subsequent
    /// calls return the cached instance.
    /// </summary>
    public async ValueTask<ReducedSpec> GetSpecAsync(CancellationToken cancellationToken = default)
    {
        if (_spec is not null)
            return _spec;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_spec is null)
            {
                var json = await _httpClient!.GetStringAsync(_options!.OpenApiPath, cancellationToken).ConfigureAwait(false);
                _spec = OpenApiReducer.Reduce(json);
            }
            return _spec;
        }
        finally
        {
            _gate.Release();
        }
    }
}
