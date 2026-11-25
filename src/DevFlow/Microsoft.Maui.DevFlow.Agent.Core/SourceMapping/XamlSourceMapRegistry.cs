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
    private CacheState _cache = new(0);

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
        while (true)
        {
            var cache = Volatile.Read(ref _cache);
            if (cache.Maps.TryGetValue(fullTypeName, out var cached))
            {
                if (ReferenceEquals(cache, Volatile.Read(ref _cache)))
                    return cached;
                continue;
            }

            IXamlSourceMapProvider[] providers;
            lock (_gate)
                providers = _providers.ToArray();

            XamlSourceMap? map = null;
            foreach (var provider in providers)
            {
                map = provider.GetMap(fullTypeName);
                if (map is not null)
                    break;
            }

            if (!ReferenceEquals(cache, Volatile.Read(ref _cache)))
                continue;
            if (map is not null)
                cache.Maps[fullTypeName] = map;
            if (ReferenceEquals(cache, Volatile.Read(ref _cache)))
                return map;
        }
    }

    internal void Reset()
    {
        lock (_gate)
        {
            _providers.Clear();
            Volatile.Write(ref _cache, new CacheState(0));
        }
    }

    internal void InvalidateCache()
    {
        lock (_gate)
            Volatile.Write(ref _cache, new CacheState(_cache.Generation + 1));
    }

    internal long Generation => Volatile.Read(ref _cache).Generation;

    internal bool HasProviders
    {
        get
        {
            lock (_gate)
                return _providers.Count > 0;
        }
    }

    private sealed class CacheState(long generation)
    {
        public long Generation { get; } = generation;
        public ConcurrentDictionary<string, XamlSourceMap> Maps { get; } =
            new(StringComparer.Ordinal);
    }
}
