using Microsoft.Maui.Controls;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Agent.Core.SourceMapping;

namespace Microsoft.Maui.DevFlow.Tests;

public class XamlSourceMapTests
{
    // ── Parser ────────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_ImplicitContent_MapsChildPathsAndTypes()
    {
        var map = XamlSourceMap.Parse("<Grid>\n  <Label />\n  <Button />\n</Grid>", "T.xaml");
        Assert.NotNull(map);
        Assert.True(map!.TryGet("", out var root));
        Assert.Equal(1, root.Line);
        Assert.Equal("Grid", root.TypeName);
        Assert.True(map.TryGet("0", out var label));
        Assert.Equal(2, label.Line);
        Assert.Equal("Label", label.TypeName);
        Assert.True(map.TryGet("1", out var button));
        Assert.Equal(3, button.Line);
        Assert.Equal("Button", button.TypeName);
    }

    [Fact]
    public void Parse_ContentPropertyElement_IsFlattened()
    {
        var map = XamlSourceMap.Parse(
            "<ContentPage>\n  <ContentPage.Content>\n    <StackLayout />\n  </ContentPage.Content>\n</ContentPage>",
            "T.xaml");
        Assert.NotNull(map);
        Assert.True(map!.TryGet("0", out var content));
        Assert.Equal("StackLayout", content.TypeName);
        Assert.Equal(3, content.Line);
    }

    [Fact]
    public void Parse_NonContentPropertyElement_IsSkipped()
    {
        var map = XamlSourceMap.Parse(
            "<Grid>\n  <Grid.RowDefinitions>\n    <RowDefinition />\n  </Grid.RowDefinitions>\n  <Label />\n</Grid>",
            "T.xaml");
        Assert.NotNull(map);
        // RowDefinitions is not a visual child; Label is content child 0 (line 5).
        Assert.True(map!.TryGet("0", out var label));
        Assert.Equal("Label", label.TypeName);
        Assert.Equal(5, label.Line);
        Assert.False(map.TryGet("0/0", out _)); // RowDefinition not mapped
    }

    [Fact]
    public void Parse_Malformed_ReturnsNull()
    {
        Assert.Null(XamlSourceMap.Parse("<Grid><Label></Grid>", "T.xaml"));
        Assert.Null(XamlSourceMap.Parse("", "T.xaml"));
    }

    // ── Type-checked mapping (hand-built trees) ─────────────────────────────────

    [Fact]
    public void ApplySourceMap_MatchingTypes_AttachesSource()
    {
        var map = Map("T.xaml", ("", 1, "Page"), ("0", 2, "Grid"), ("0/0", 3, "Label"));
        var walker = WalkerWith("Root.Page", map);
        var tree = Node("Root.Page", "Page", Node("Grid", "Grid", Node("Label", "Label")));

        walker.ApplySourceMap(new[] { tree });

        Assert.Equal(1, tree.SourceLine);
        Assert.Equal(2, tree.Children![0].SourceLine);
        Assert.Equal(3, tree.Children![0].Children![0].SourceLine);
        Assert.Equal("T.xaml", tree.Children![0].Children![0].SourceFile);
    }

    [Fact]
    public void ApplySourceMap_TypeMismatch_NullsElementAndSubtree()
    {
        // Map expects a Grid at "0" with a Label under it, but the runtime child is a StackLayout
        // (an index-shift/wrong-type situation). The mismatch must null it AND its subtree.
        var map = Map("T.xaml", ("", 1, "Page"), ("0", 2, "Grid"), ("0/0", 3, "Label"));
        var walker = WalkerWith("Root.Page", map);
        var tree = Node("Root.Page", "Page", Node("SL", "StackLayout", Node("Btn", "Button")));

        walker.ApplySourceMap(new[] { tree });

        Assert.Equal(1, tree.SourceLine);                      // root still attaches (provider match)
        Assert.Null(tree.Children![0].SourceFile);             // StackLayout != Grid → null
        Assert.Null(tree.Children[0].Children![0].SourceFile); // subtree stays null (no wrong line)
    }

    [Fact]
    public void ApplySourceMap_NestedXamlView_UsesParentUsageThenOwnMap()
    {
        var parent = Map("Parent.xaml", ("", 1, "Page"), ("0", 5, "MyView"));
        var child = Map("MyView.xaml", ("", 1, "MyView"), ("0", 2, "Label"));
        var provider = new TestMapProvider();
        provider.Add("Root.Page", parent);
        provider.Add("Lib.MyView", child);
        var walker = new VisualTreeWalker { SourceMapProvider = provider };
        var tree = Node("Root.Page", "Page", Node("Lib.MyView", "MyView", Node("Label", "Label")));

        walker.ApplySourceMap(new[] { tree });

        var myView = tree.Children![0];
        Assert.Equal("Parent.xaml", myView.SourceFile); // usage: the <local:MyView/> line in the parent
        Assert.Equal(5, myView.SourceLine);
        var label = myView.Children![0];
        Assert.Equal("MyView.xaml", label.SourceFile);   // descendants: MyView's own map
        Assert.Equal(2, label.SourceLine);
    }

    [Fact]
    public void ApplySourceMap_SyntheticChild_SkippedWithoutShiftingIndices()
    {
        var map = Map("T.xaml", ("", 1, "Page"), ("0", 2, "Label"));
        var walker = WalkerWith("Root.Page", map);
        // A synthetic element is appended after the real child; it must be ignored and must not
        // consume the content index, so the real Label still maps to path "0".
        var synthetic = Node("syn", "BackButton");
        synthetic.FullType = "Microsoft.Maui.DevFlow.Agent.Core.BackButton";
        var page = Node("Root.Page", "Page", Node("Label", "Label"), synthetic);

        walker.ApplySourceMap(new[] { page });

        Assert.Equal(2, page.Children![0].SourceLine); // Label mapped
        Assert.Null(page.Children[1].SourceFile);      // synthetic gets nothing
    }

    [Fact]
    public void ApplySourceMap_RuntimeFrameworkExtras_DoNotBreakNestedContentMapping()
    {
        var map = Map(
            "MainPage.xaml",
            ("", 1, "Page"),
            ("0", 2, "Grid"),
            ("0/0", 3, "Border"),
            ("0/0/0", 4, "Grid"),
            ("0/0/0/0", 5, "Button"));
        var walker = WalkerWith("Root.Page", map);

        var addButton = Node("Button", "Button");
        var innerGrid = Node("Grid", "Grid", addButton);
        var strokeShape = Node("RoundRectangle", "RoundRectangle");
        var border = Node("Border", "Border", strokeShape, innerGrid);
        var outerGrid = Node("Grid", "Grid", border);
        var toolbarItem = Node("ToolbarItem", "ToolbarItem");
        var page = Node("Root.Page", "Page", outerGrid, toolbarItem);

        walker.ApplySourceMap(new[] { page });

        Assert.Equal(2, outerGrid.SourceLine);
        Assert.Equal(3, border.SourceLine);
        Assert.Null(strokeShape.SourceFile);
        Assert.Equal(4, innerGrid.SourceLine);
        Assert.Equal(5, addButton.SourceLine);
        Assert.Null(toolbarItem.SourceFile);
    }

    [Fact]
    public void ApplySourceMap_InterleavedRuntimeExtras_MapUniqueContentSequence()
    {
        var map = Map("T.xaml", ("", 1, "Page"), ("0", 2, "Label"), ("1", 3, "Button"));
        var walker = WalkerWith("Root.Page", map);
        var label = Node("Label", "Label");
        var button = Node("Button", "Button");
        var page = Node(
            "Root.Page",
            "Page",
            label,
            Node("RoundRectangle", "RoundRectangle"),
            button,
            Node("ToolbarItem", "ToolbarItem"));

        walker.ApplySourceMap(new[] { page });

        Assert.Equal(2, label.SourceLine);
        Assert.Null(page.Children![1].SourceFile);
        Assert.Equal(3, button.SourceLine);
        Assert.Null(page.Children[3].SourceFile);
    }

    [Fact]
    public void ApplySourceMap_LeafWithRuntimeExtra_MapsParentOnly()
    {
        var map = Map("T.xaml", ("", 1, "Page"));
        var walker = WalkerWith("Root.Page", map);
        var extra = Node("RoundRectangle", "RoundRectangle");
        var page = Node("Root.Page", "Page", extra);

        walker.ApplySourceMap(new[] { page });

        Assert.Equal(1, page.SourceLine);
        Assert.Null(extra.SourceFile);
    }

    [Fact]
    public void ApplySourceMap_ChildCountMismatch_NullsChildren()
    {
        // Map: Grid at "0" has ONE Label child. Runtime Grid has TWO Labels (a same-type insertion)
        // — there are two valid alignments, so both children must stay null (no wrong line).
        var map = Map("T.xaml", ("", 1, "Page"), ("0", 2, "Grid"), ("0/0", 3, "Label"));
        var walker = WalkerWith("Root.Page", map);
        var tree = Node("Root.Page", "Page", Node("Grid", "Grid", Node("L1", "Label"), Node("L2", "Label")));

        walker.ApplySourceMap(new[] { tree });

        Assert.Equal(2, tree.Children![0].SourceLine);          // Grid still maps
        Assert.Null(tree.Children![0].Children![0].SourceFile); // children nulled (count shifted)
        Assert.Null(tree.Children![0].Children![1].SourceFile);
    }

    [Fact]
    public void ApplySourceMap_NoProvider_IsNoOp()
    {
        var walker = new VisualTreeWalker();
        var tree = Node("Root.Page", "Page", Node("Grid", "Grid"));
        walker.ApplySourceMap(new[] { tree }); // must not throw
        Assert.Null(tree.SourceFile);
    }

    // ── End-to-end: real MAUI tree (real GetVisualChildren order vs parser paths) ─

    [Fact]
    public void EndToEnd_RealMauiTree_ParserPathsMatchRuntimeOrder()
    {
        // Build a real MAUI tree and the equivalent XAML; the parser's child-paths must line up
        // with the runtime GetVisualChildren() order for the source to attach to the right elements.
        var grid = new Grid { Children = { new Label(), new Button() } };
        const string xaml = "<Grid>\n  <Label />\n  <Button />\n</Grid>";
        var map = XamlSourceMap.Parse(xaml, "Page.xaml")!;

        var provider = new TestMapProvider();
        provider.Add(grid.GetType().FullName!, map);
        var walker = new VisualTreeWalker { SourceMapProvider = provider };

        var info = walker.WalkElement(grid, null, 1, 0);
        Assert.NotNull(info);
        walker.ApplySourceMap(new[] { info! });

        Assert.Equal(1, info!.SourceLine);           // <Grid>
        Assert.Equal("Label", info.Children![0].Type);
        Assert.Equal(2, info.Children[0].SourceLine); // <Label>
        Assert.Equal("Button", info.Children[1].Type);
        Assert.Equal(3, info.Children[1].SourceLine); // <Button>
        Assert.Equal("Page.xaml", info.Children[0].SourceFile);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private static ElementInfo Node(string id, string type, params ElementInfo[] children) => new()
    {
        Id = id,
        Type = type,
        FullType = id, // tests key the provider by this
        Children = children.Length > 0 ? new List<ElementInfo>(children) : null,
    };

    private static XamlSourceMap Map(string file, params (string Path, int Line, string Type)[] entries)
    {
        static string ParentOf(string path)
        {
            var i = path.LastIndexOf('/');
            return i < 0 ? "" : path[..i];
        }

        var dict = entries.ToDictionary(
            e => e.Path,
            e => new XamlSourceEntry(e.Line, 1, e.Type, entries.Count(c => c.Path.Length > 0 && ParentOf(c.Path) == e.Path)),
            StringComparer.Ordinal);
        return new XamlSourceMap(file, dict);
    }

    private static VisualTreeWalker WalkerWith(string rootFullType, XamlSourceMap map)
    {
        var provider = new TestMapProvider();
        provider.Add(rootFullType, map);
        return new VisualTreeWalker { SourceMapProvider = provider };
    }

    private sealed class TestMapProvider : IXamlSourceMapProvider
    {
        private readonly Dictionary<string, XamlSourceMap> _maps = new(StringComparer.Ordinal);
        public void Add(string fullTypeName, XamlSourceMap map) => _maps[fullTypeName] = map;
        public XamlSourceMap? GetMap(string fullTypeName) => _maps.TryGetValue(fullTypeName, out var m) ? m : null;
    }
}
