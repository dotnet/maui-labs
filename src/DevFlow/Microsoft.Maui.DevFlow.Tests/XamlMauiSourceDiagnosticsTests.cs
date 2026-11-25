using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Agent.Core.SourceMapping;

namespace Microsoft.Maui.DevFlow.Tests;

[Collection("XamlSourceMapRegistry")]
public sealed class XamlMauiSourceDiagnosticsTests : IDisposable
{
    private const string DiagnosticsSwitch = "Microsoft.Maui.RuntimeFeature.EnableMauiDiagnostics";
    private static readonly string Xaml = """
        <ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui">
          <VerticalStackLayout>
            <Label Text="Same" />
            <Label Text="Same" />
          </VerticalStackLayout>
        </ContentPage>
        """.ReplaceLineEndings("\n");
    private const string SourcePath = "Views/MainPage.xaml";
    private static readonly string AssemblyName = typeof(XamlMauiSourceDiagnosticsTests).Assembly.GetName().Name!;
    private readonly bool _previousDiagnostics;

    public XamlMauiSourceDiagnosticsTests()
    {
        _previousDiagnostics = AppContext.TryGetSwitch(DiagnosticsSwitch, out var enabled)
            ? enabled
            : AppContext.TryGetSwitch("Microsoft.Maui.RuntimeFeature.EnableDiagnostics", out enabled) && enabled;
        AppContext.SetSwitch(DiagnosticsSwitch, true);
        XamlSourceMapRegistry.Instance.Reset();
    }

    public void Dispose()
    {
        XamlSourceMapRegistry.Instance.Reset();
        AppContext.SetSwitch(DiagnosticsSwitch, _previousDiagnostics);
    }

    [Fact]
    public void MauiSourceInfo_IdentifiesReorderedSameTextSiblings()
    {
        var first = new Label { Text = "Same" };
        var second = new Label { Text = "Same" };
        Register(first, 3);
        Register(second, 4);
        var map = Parse(Xaml);
        var walker = new VisualTreeWalker { SourceMapProvider = new MapProvider(map) };
        var root = Tree(second, first);

        walker.ApplySourceMap([root]);

        var labels = root.Children![0].Children!;
        Assert.Equal(new int?[] { 4, 3 }, labels.Select(label => label.SourceLine));
        Assert.All(labels, label =>
        {
            Assert.Equal(SourcePath, label.SourceFile);
            Assert.Equal(6, label.SourceColumn);
            Assert.Equal(map.ContentHash, label.SourceHash);
        });
    }

    [Theory]
    [InlineData("Views/Other.xaml", null)]
    [InlineData("Views/MainPage.xaml", "OtherAssembly")]
    public void MauiSourceInfo_FromAnotherDocument_DoesNotUseTheInferredLocation(
        string path, string? assembly)
    {
        var label = new Label { Text = "Same" };
        Register(label, 3, path, assembly ?? AssemblyName);
        const string declaration = "    <Label Text=\"Same\" />\n";
        var map = Parse(Xaml.Remove(Xaml.LastIndexOf(declaration, StringComparison.Ordinal), declaration.Length));
        var walker = new VisualTreeWalker { SourceMapProvider = new MapProvider(map) };
        var root = Tree(label);

        walker.ApplySourceMap([root]);

        Assert.Null(root.Children![0].Children![0].SourceFile);
    }

    [Fact]
    public void MissingMauiSourceInfo_UsesTheExistingMap()
    {
        var first = new Label { Text = "First" };
        var second = new Label { Text = "Second" };
        Assert.Null(VisualDiagnostics.GetSourceInfo(first));
        var xaml = Xaml.Replace("Text=\"Same\"", "Text=\"First\"", StringComparison.Ordinal);
        var last = xaml.LastIndexOf("Text=\"First\"", StringComparison.Ordinal);
        xaml = xaml.Remove(last, "Text=\"First\"".Length).Insert(last, "Text=\"Second\"");
        var walker = new VisualTreeWalker { SourceMapProvider = new MapProvider(Parse(xaml)) };
        var root = Tree(first, second);

        walker.ApplySourceMap([root]);

        Assert.Equal(new int?[] { 3, 4 }, root.Children![0].Children!.Select(label => label.SourceLine));
    }

    [Fact]
    public void HotReload_DoesNotPairStaleMauiLocationsWithTheNewHash()
    {
        var first = new Label { Text = "Same" };
        var second = new Label { Text = "Same" };
        Register(first, 3);
        Register(second, 4);
        var provider = new MapProvider(Parse(Xaml));
        var walker = new VisualTreeWalker { SourceMapProvider = provider };
        var page = new ContentPage();
        var layout = new VerticalStackLayout();
        var before = Tree(page, layout, first, second);
        walker.ApplySourceMap([before]);
        Assert.Equal(new int?[] { 3, 4 }, before.Children![0].Children!.Select(label => label.SourceLine));

        provider.Map = Parse(Xaml.Replace(
            "  <VerticalStackLayout>\n",
            "  <VerticalStackLayout>\n    <!-- moved declarations -->\n",
            StringComparison.Ordinal));
        XamlSourceMapHotReloadHandler.ClearCache(null);
        var after = Tree(page, layout, first, second);

        walker.ApplySourceMap([after]);

        Assert.All(after.Children![0].Children!, label =>
        {
            Assert.Null(label.SourceLine);
            Assert.Null(label.SourceHash);
        });
    }

    [Fact]
    public void HotReload_UsesTheUpdatedMapWhenFallbackCanIdentifySiblings()
    {
        var xaml = Xaml.Replace("Text=\"Same\"", "Text=\"First\"", StringComparison.Ordinal);
        var last = xaml.LastIndexOf("Text=\"First\"", StringComparison.Ordinal);
        xaml = xaml.Remove(last, "Text=\"First\"".Length).Insert(last, "Text=\"Second\"");
        var first = new Label { Text = "First" };
        var second = new Label { Text = "Second" };
        Register(first, 3);
        Register(second, 4);
        var provider = new MapProvider(Parse(xaml));
        var walker = new VisualTreeWalker { SourceMapProvider = provider };
        var page = new ContentPage();
        var layout = new VerticalStackLayout();
        walker.ApplySourceMap([Tree(page, layout, first, second)]);

        provider.Map = Parse(xaml.Replace(
            "  <VerticalStackLayout>\n",
            "  <VerticalStackLayout>\n    <!-- moved declarations -->\n",
            StringComparison.Ordinal));
        XamlSourceMapHotReloadHandler.ClearCache(null);
        var after = Tree(page, layout, first, second);

        walker.ApplySourceMap([after]);

        var labels = after.Children![0].Children!;
        Assert.Equal(new int?[] { 4, 5 }, labels.Select(label => label.SourceLine));
        Assert.All(labels, label => Assert.Equal(provider.Map.ContentHash, label.SourceHash));
    }

    [Fact]
    public void HotReload_ChangedUnnamedDeclaration_DoesNotClaimTheOldObject()
    {
        const string declaration = "    <Label Text=\"Same\" />\n";
        var xaml = Xaml.Remove(Xaml.LastIndexOf(declaration, StringComparison.Ordinal), declaration.Length);
        var label = new Label { Text = "Same" };
        Register(label, 3);
        var provider = new MapProvider(Parse(xaml));
        var walker = new VisualTreeWalker { SourceMapProvider = provider };
        var page = new ContentPage();
        var layout = new VerticalStackLayout();
        var before = Tree(page, layout, label);
        walker.ApplySourceMap([before]);
        Assert.Equal(3, before.Children![0].Children![0].SourceLine);

        provider.Map = Parse(xaml.Replace("Text=\"Same\"", "Text=\"Replacement\"", StringComparison.Ordinal));
        XamlSourceMapHotReloadHandler.ClearCache(null);
        var after = Tree(page, layout, label);

        walker.ApplySourceMap([after]);

        Assert.Null(after.Children![0].Children![0].SourceHash);
    }

    [Fact]
    public void HotReload_ReplacementObject_DoesNotInheritPreviousSourceIdentity()
    {
        const string declaration = "    <Label Text=\"Same\" />\n";
        var xaml = Xaml.Remove(Xaml.LastIndexOf(declaration, StringComparison.Ordinal), declaration.Length);
        var original = new Label { Text = "Same" };
        Register(original, 3);
        var provider = new MapProvider(Parse(xaml));
        var walker = new VisualTreeWalker { SourceMapProvider = provider };
        var page = new ContentPage();
        var layout = new VerticalStackLayout();
        walker.ApplySourceMap([Tree(page, layout, original)]);

        provider.Map = Parse(xaml.Replace(
            "  <VerticalStackLayout>\n",
            "  <VerticalStackLayout>\n    <!-- moved declaration -->\n",
            StringComparison.Ordinal));
        XamlSourceMapHotReloadHandler.ClearCache(null);
        var replacement = new Label { Text = "Same" };
        var after = Tree(page, layout, replacement);

        walker.ApplySourceMap([after]);

        Assert.Null(after.Children![0].Children![0].SourceHash);
    }

    [Fact]
    public void HotReload_ReparentedObject_DoesNotClaimAnotherDeclaration()
    {
        const string declaration = "    <Label Text=\"Same\" />\n";
        var xaml = Xaml.Remove(Xaml.LastIndexOf(declaration, StringComparison.Ordinal), declaration.Length);
        var label = new Label { Text = "Same" };
        Register(label, 3);
        var provider = new MapProvider(Parse(xaml));
        var walker = new VisualTreeWalker { SourceMapProvider = provider };
        var page = new ContentPage();
        walker.ApplySourceMap([Tree(page, new VerticalStackLayout(), label)]);

        provider.Map = Parse(xaml + "\n<!-- updated -->");
        XamlSourceMapHotReloadHandler.ClearCache(null);
        var after = Tree(page, new VerticalStackLayout(), label);

        walker.ApplySourceMap([after]);

        Assert.Null(after.Children![0].Children![0].SourceHash);
    }

    [Fact]
    public async Task HotReload_RacingWalk_DoesNotCommitUnverifiedIdentity()
    {
        const string declaration = "    <Label Text=\"Same\" />\n";
        var xaml = Xaml.Remove(Xaml.LastIndexOf(declaration, StringComparison.Ordinal), declaration.Length);
        using var provider = new BlockingMapProvider(Parse(xaml));
        var walker = new VisualTreeWalker { SourceMapProvider = provider };
        var label = new Label { Text = "Same" };
        var root = Tree(label);
        var walk = Task.Run(() => walker.ApplySourceMap([root]));
        try
        {
            Assert.True(provider.Started.Wait(TimeSpan.FromSeconds(10)));
            provider.Map = Parse(xaml + "\n<!-- updated -->");
            XamlSourceMapHotReloadHandler.ClearCache(null);
        }
        finally
        {
            provider.Continue.Set();
        }

        await walk;
        Assert.Null(root.Children![0].Children![0].SourceHash);
    }

    [Fact]
    public void HotReload_UnobservedIntermediateUpdate_DoesNotReuseOlderIdentity()
    {
        const string declaration = "    <Label Text=\"Same\" />\n";
        var xaml = Xaml.Remove(Xaml.LastIndexOf(declaration, StringComparison.Ordinal), declaration.Length);
        var label = new Label { Text = "Same" };
        Register(label, 3);
        var provider = new MapProvider(Parse(xaml));
        var walker = new VisualTreeWalker { SourceMapProvider = provider };
        var page = new ContentPage();
        var layout = new VerticalStackLayout();
        walker.ApplySourceMap([Tree(page, layout, label)]);

        provider.Map = Parse(xaml.Replace("Text=\"Same\"", "Text=\"Replacement\"", StringComparison.Ordinal));
        XamlSourceMapHotReloadHandler.ClearCache(null);
        provider.Map = Parse(xaml + "\n<!-- another update -->");
        XamlSourceMapHotReloadHandler.ClearCache(null);
        var after = Tree(page, layout, label);

        walker.ApplySourceMap([after]);

        Assert.Null(after.Children![0].Children![0].SourceHash);
    }

    [Fact]
    public void HotReload_ChangedMauiRegistration_DoesNotReuseOlderIdentity()
    {
        const string declaration = "    <Label Text=\"Same\" />\n";
        var xaml = Xaml.Remove(Xaml.LastIndexOf(declaration, StringComparison.Ordinal), declaration.Length);
        var label = new Label { Text = "Same" };
        Register(label, 3);
        var provider = new MapProvider(Parse(xaml));
        var walker = new VisualTreeWalker { SourceMapProvider = provider };
        var page = new ContentPage();
        var layout = new VerticalStackLayout();
        walker.ApplySourceMap([Tree(page, layout, label)]);

        Register(label, 3, "Views/Other.xaml");
        provider.Map = Parse(xaml + "\n<!-- updated -->");
        XamlSourceMapHotReloadHandler.ClearCache(null);
        var after = Tree(page, layout, label);

        walker.ApplySourceMap([after]);

        Assert.Null(after.Children![0].Children![0].SourceHash);
    }

    [Fact]
    public void HotReload_ChangedDeclarationWithStableAutomationId_RemainsMapped()
    {
        const string declaration = "    <Label Text=\"Same\" />\n";
        var xaml = Xaml.Remove(Xaml.LastIndexOf(declaration, StringComparison.Ordinal), declaration.Length)
            .Replace("Text=\"Same\"", "AutomationId=\"stable\" Text=\"Same\"", StringComparison.Ordinal);
        var label = new Label { AutomationId = "stable", Text = "Same" };
        Register(label, 3);
        var provider = new MapProvider(Parse(xaml));
        var walker = new VisualTreeWalker { SourceMapProvider = provider };
        var page = new ContentPage();
        var layout = new VerticalStackLayout();
        walker.ApplySourceMap([Tree(page, layout, label)]);

        provider.Map = Parse(xaml.Replace("Text=\"Same\"", "Text=\"Updated\"", StringComparison.Ordinal));
        label.Text = "Updated";
        XamlSourceMapHotReloadHandler.ClearCache(null);
        var after = Tree(page, layout, label);

        walker.ApplySourceMap([after]);

        var source = after.Children![0].Children![0];
        Assert.Equal(3, source.SourceLine);
        Assert.Equal(provider.Map.ContentHash, source.SourceHash);
    }

    private static XamlSourceMap Parse(string xaml)
        => XamlSourceMap.Parse(xaml, SourcePath, null, null, null, SourcePath, AssemblyName)!;

    private static void Register(Label label, int line, string path = SourcePath, string? assembly = null)
        => VisualDiagnostics.RegisterSourceInfo(
            label,
            new Uri($"{path};assembly={assembly ?? AssemblyName}", UriKind.Relative),
            line,
            6);

    private static ElementInfo Tree(params Label[] labels)
        => Tree(new ContentPage(), new VerticalStackLayout(), labels);

    private static ElementInfo Tree(ContentPage page, VerticalStackLayout layout, params Label[] labels) => new()
    {
        Id = "page",
        Type = "ContentPage",
        FullType = "Test.Page",
        IdentityToken = page,
        Children =
        [
            new ElementInfo
            {
                Id = "layout",
                Type = "VerticalStackLayout",
                FullType = typeof(VerticalStackLayout).FullName!,
                IdentityToken = layout,
                Children = labels.Select((label, index) => new ElementInfo
                {
                    Id = $"label-{index}",
                    Type = "Label",
                    FullType = typeof(Label).FullName!,
                    IdentityToken = label,
                    AutomationId = label.AutomationId,
                    Text = label.Text
                }).ToList()
            }
        ]
    };

    private sealed class MapProvider(XamlSourceMap map) : IXamlSourceMapProvider
    {
        public XamlSourceMap Map { get; set; } = map;
        public XamlSourceMap? GetMap(string fullTypeName) => fullTypeName == "Test.Page" ? Map : null;
    }

    private sealed class BlockingMapProvider(XamlSourceMap map) : IXamlSourceMapProvider, IDisposable
    {
        public readonly ManualResetEventSlim Started = new();
        public readonly ManualResetEventSlim Continue = new();
        public XamlSourceMap Map = map;

        public XamlSourceMap? GetMap(string fullTypeName)
        {
            if (fullTypeName != "Test.Page")
                return null;
            var result = Map;
            Started.Set();
            if (!Continue.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("Timed out waiting to finish the map lookup.");
            return result;
        }

        public void Dispose()
        {
            Started.Dispose();
            Continue.Dispose();
        }
    }
}
