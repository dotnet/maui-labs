using System.Collections.Concurrent;

namespace Microsoft.Maui.DevFlow.Agent.Core.SourceMapping;

/// <summary>
/// Process-wide registry of <see cref="IXamlSourceMapProvider"/>s and the default
/// <see cref="VisualTreeWalker.SourceMapProvider"/>. The build-time XAML source-map generator emits
/// a <c>[ModuleInitializer]</c> in each app/library assembly that calls <see cref="Register"/> with
/// that assembly's generated provider; the runtime walker queries <see cref="Instance"/> to attach
/// source locations. Thread-safe, and safe to call before or after the agent starts. When no
/// provider has registered, <see cref="GetMap"/> returns null, so source mapping is a no-op.
/// </summary>
public sealed class XamlSourceMapRegistry : IXamlSourceMapProvider
{
    /// <summary>The shared registry used by generated module initializers and the runtime walker.</summary>
    public static XamlSourceMapRegistry Instance { get; } = new();

    private readonly List<IXamlSourceMapProvider> _providers = new();
    private readonly object _gate = new();

    // POSITIVE cache only (fullTypeName -> map). We deliberately never cache a null result: a
    // provider can register LATE (another assembly's module initializer may run after the first
    // GetMap for one of its types), so an as-yet-unmapped type must stay queryable. Parsed maps are
    // immutable and stable per type, so caching hits is safe.
    private readonly ConcurrentDictionary<string, XamlSourceMap> _cache = new(StringComparer.Ordinal);

    private XamlSourceMapRegistry() { }

    /// <summary>
    /// Registers a source-map provider. Idempotent by reference (safe under hot-reload / repeated
    /// module initialization). Called by the generator-emitted <c>[ModuleInitializer]</c>.
    /// </summary>
    public static void Register(IXamlSourceMapProvider provider)
    {
        if (provider is null) return;
        var registry = Instance;
        lock (registry._gate)
        {
            if (!registry._providers.Contains(provider))
                registry._providers.Add(provider);
        }
    }

    /// <inheritdoc />
    public XamlSourceMap? GetMap(string fullTypeName)
    {
        if (string.IsNullOrEmpty(fullTypeName)) return null;
        if (_cache.TryGetValue(fullTypeName, out var cached)) return cached;

        IXamlSourceMapProvider[] snapshot;
        lock (_gate)
        {
            if (_providers.Count == 0) return null;
            snapshot = _providers.ToArray();
        }

        foreach (var provider in snapshot)
        {
            var map = provider.GetMap(fullTypeName);
            if (map is not null)
            {
                // Positive cache only. First non-null wins (registration order).
                _cache[fullTypeName] = map;
                return map;
            }
        }

        return null;
    }

    /// <summary>Clears all registered providers and the cache. For tests and hot-reload teardown.</summary>
    internal void Reset()
    {
        lock (_gate)
            _providers.Clear();
        _cache.Clear();
    }

    /// <summary>True when at least one provider is registered (maps may be available).</summary>
    internal bool HasProviders
    {
        get { lock (_gate) return _providers.Count > 0; }
    }
}
