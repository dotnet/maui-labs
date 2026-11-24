using System.Collections.Concurrent;
using System.Reflection.Metadata;

[assembly: MetadataUpdateHandler(typeof(Microsoft.Maui.DevFlow.Agent.Core.SourceMapping.XamlSourceMapHotReloadHandler))]

namespace Microsoft.Maui.DevFlow.Agent.Core.SourceMapping;

static class XamlSourceMapHotReloadHandler
{
    internal static void ClearCache(Type[]? updatedTypes)
        => XamlSourceMapRegistry.Instance.InvalidateCache();
}

public sealed class XamlSourceMapRegistry : IXamlSourceMapProvider
{
    public static XamlSourceMapRegistry Instance { get; } = new();

    private readonly List<IXamlSourceMapProvider> _providers = [];
    private readonly object _gate = new();
    private ConcurrentDictionary<string, XamlSourceMap> _cache =
        new(StringComparer.Ordinal);

    private XamlSourceMapRegistry()
    {
    }

    public static void Register(IXamlSourceMapProvider provider)
    {
        if (provider is null)
            return;

        var registry = Instance;
        lock (registry._gate)
        {
            if (!registry._providers.Contains(provider))
                registry._providers.Add(provider);
        }
    }

    public XamlSourceMap? GetMap(string fullTypeName)
    {
        if (string.IsNullOrEmpty(fullTypeName))
            return null;
        var cache = Volatile.Read(ref _cache);
        if (cache.TryGetValue(fullTypeName, out var cached))
            return cached;

        IXamlSourceMapProvider[] providers;
        lock (_gate)
        {
            if (_providers.Count == 0)
                return null;
            providers = _providers.ToArray();
        }

        foreach (var provider in providers)
        {
            var map = provider.GetMap(fullTypeName);
            if (map is null)
                continue;
            cache[fullTypeName] = map;
            return map;
        }

        return null;
    }

    internal void Reset()
    {
        lock (_gate)
            _providers.Clear();
        InvalidateCache();
    }

    // A lookup already in flight must not repopulate the new cache with its old result.
    internal void InvalidateCache()
        => Interlocked.Exchange(ref _cache, new(StringComparer.Ordinal));

    internal bool HasProviders
    {
        get
        {
            lock (_gate)
                return _providers.Count > 0;
        }
    }
}
