using Microsoft.Maui.DevFlow.Agent.Core.SourceMapping;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// Verifies <see cref="XamlSourceMapRegistry"/> composition semantics: no-op when empty,
/// first-non-null-wins, reference-idempotent registration, and — critically — that it does NOT
/// negatively cache, so a provider registering late (another assembly's module initializer) is
/// still discovered.
/// </summary>
[Collection("XamlSourceMapRegistry")]
public class XamlSourceMapRegistryTests : System.IDisposable
{
    public XamlSourceMapRegistryTests() => XamlSourceMapRegistry.Instance.Reset();
    public void Dispose() => XamlSourceMapRegistry.Instance.Reset();

    private static XamlSourceMap MapFor(string file)
        => new(file, new Dictionary<string, XamlSourceEntry> { [""] = new(1, 2, "ContentPage", 0) });

    [Fact]
    public void GetMap_NoProviders_ReturnsNull()
    {
        Assert.Null(XamlSourceMapRegistry.Instance.GetMap("Any.Type"));
    }

    [Fact]
    public void Register_ThenGetMap_ReturnsProviderMap()
    {
        var provider = new FakeProvider { ["App.MainPage"] = MapFor("MainPage.xaml") };
        XamlSourceMapRegistry.Register(provider);

        var map = XamlSourceMapRegistry.Instance.GetMap("App.MainPage");
        Assert.NotNull(map);
        Assert.Equal("MainPage.xaml", map!.File);
    }

    [Fact]
    public void GetMap_FirstNonNullWins()
    {
        var first = new FakeProvider { ["App.Page"] = MapFor("first.xaml") };
        var second = new FakeProvider { ["App.Page"] = MapFor("second.xaml") };
        XamlSourceMapRegistry.Register(first);
        XamlSourceMapRegistry.Register(second);

        Assert.Equal("first.xaml", XamlSourceMapRegistry.Instance.GetMap("App.Page")!.File);
    }

    [Fact]
    public void Register_IsIdempotentByReference()
    {
        // P maps nothing (always queried, counts calls); Q supplies the map. If P were listed
        // twice (dedup failure), it would be queried twice before Q is reached.
        var p = new FakeProvider();
        var q = new FakeProvider { ["App.Page"] = MapFor("page.xaml") };
        XamlSourceMapRegistry.Register(p);
        XamlSourceMapRegistry.Register(p); // same reference again
        XamlSourceMapRegistry.Register(q);

        Assert.NotNull(XamlSourceMapRegistry.Instance.GetMap("App.Page"));
        Assert.Equal(1, p.CallCount); // deduped → queried exactly once
    }

    [Fact]
    public void GetMap_PositiveCache_ReturnsSameInstanceAndQueriesOnce()
    {
        var provider = new FakeProvider { ["App.Page"] = MapFor("page.xaml") };
        XamlSourceMapRegistry.Register(provider);

        var a = XamlSourceMapRegistry.Instance.GetMap("App.Page");
        var b = XamlSourceMapRegistry.Instance.GetMap("App.Page");
        Assert.Same(a, b);
        Assert.Equal(1, provider.CallCount);
    }

    [Fact]
    public void GetMap_DoesNotNegativeCache_LateRegistrationDiscovered()
    {
        // Query a type before any provider maps it → null (must NOT be cached as null).
        Assert.Null(XamlSourceMapRegistry.Instance.GetMap("Lib.LateView"));

        // A library's module initializer registers its provider later.
        XamlSourceMapRegistry.Register(new FakeProvider { ["Lib.LateView"] = MapFor("late.xaml") });

        // The previously-null type is now discoverable.
        Assert.NotNull(XamlSourceMapRegistry.Instance.GetMap("Lib.LateView"));
    }

    [Fact]
    public void GetMap_NullOrEmptyType_ReturnsNull()
    {
        XamlSourceMapRegistry.Register(new FakeProvider { ["App.Page"] = MapFor("page.xaml") });
        Assert.Null(XamlSourceMapRegistry.Instance.GetMap(""));
    }

    [Fact]
    public void Register_NullProvider_Ignored()
    {
        XamlSourceMapRegistry.Register(null!);
        Assert.Null(XamlSourceMapRegistry.Instance.GetMap("Any.Type"));
    }

    private sealed class FakeProvider : IXamlSourceMapProvider
    {
        private readonly Dictionary<string, XamlSourceMap> _maps = new(System.StringComparer.Ordinal);
        public int CallCount { get; private set; }
        public XamlSourceMap this[string type] { set => _maps[type] = value; }

        public XamlSourceMap? GetMap(string fullTypeName)
        {
            CallCount++;
            return _maps.TryGetValue(fullTypeName, out var map) ? map : null;
        }
    }
}
