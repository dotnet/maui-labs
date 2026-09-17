using System.Collections.Concurrent;

namespace Microsoft.Maui.DevFlow.Agent.Core.SourceMapping;

public sealed class XamlSourceMapRegistry : IXamlSourceMapProvider
{
    public static XamlSourceMapRegistry Instance { get; } = new();

    private readonly List<IXamlSourceMapProvider> _providers = [];
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<string, XamlSourceMap> _cache =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, XamlSourceMap> _overrides =
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
        if (_overrides.TryGetValue(fullTypeName, out var reloaded))
            return reloaded;
        if (_cache.TryGetValue(fullTypeName, out var cached))
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
            _cache[fullTypeName] = map;
            return map;
        }

        return null;
    }

    /// <summary>
    /// Replaces the map for a type whose XAML was reloaded at runtime, so source locations and
    /// <see cref="XamlSourceMap.ContentHash"/> describe the text that is now running rather than
    /// the text the app was built from.
    /// </summary>
    internal static void Override(string fullTypeName, XamlSourceMap map)
    {
        if (string.IsNullOrEmpty(fullTypeName) || map is null)
            return;

        Instance._overrides[fullTypeName] = map;
    }

    internal void Reset()
    {
        lock (_gate)
            _providers.Clear();
        _cache.Clear();
        _overrides.Clear();
    }

    internal bool HasProviders
    {
        get
        {
            lock (_gate)
                return _providers.Count > 0 || !_overrides.IsEmpty;
        }
    }
}
