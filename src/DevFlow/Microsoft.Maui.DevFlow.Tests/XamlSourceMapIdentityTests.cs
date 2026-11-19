using System.Reflection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Agent.Core.SourceMapping;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// Per-instance identity (AutomationId + resolved full CLR type) that closes the same-type
/// same-count sibling reorder ambiguity and clr-namespace short-name collisions, plus the source
/// content hash. Identity is additive, so entries without it retain their existing behavior.
/// </summary>
public class XamlSourceMapIdentityTests
{
    private const string Ns =
        "xmlns=\"http://schemas.microsoft.com/dotnet/2021/maui\"\n" +
        "             xmlns:x=\"http://schemas.microsoft.com/winfx/2009/xaml\"\n" +
        "             xmlns:local=\"clr-namespace:MyApp.Controls\"\n" +
        "             x:Class=\"MyApp.MainPage\"";

    // ── parser ──

    [Fact]
    public void Parse_ExtractsAutomationId_And_ClrNamespaceFullType()
    {
        var xaml =
            $"<ContentPage {Ns}>\n" +
            "    <VerticalStackLayout>\n" +
            "        <Label AutomationId=\"title\" Text=\"Hi\" />\n" +
            "        <local:Card AutomationId=\"card\" />\n" +
            "    </VerticalStackLayout>\n" +
            "</ContentPage>\n";
        var map = XamlSourceMap.Parse(xaml, "MainPage.xaml")!;

        Assert.True(map.TryGet("0/0", out var label));
        Assert.Equal("title", label.AutomationId);
        Assert.Equal("Label", label.TypeName);
        Assert.Null(label.FullTypeName); // MAUI-schema namespace → short name only

        Assert.True(map.TryGet("0/1", out var card));
        Assert.Equal("card", card.AutomationId);
        Assert.Equal("MyApp.Controls.Card", card.FullTypeName); // clr-namespace resolved

        Assert.False(string.IsNullOrEmpty(map.ContentHash));
    }

    [Fact]
    public void Parse_DuplicateSiblingAutomationId_NotTrustedAsIdentity()
    {
        var xaml =
            $"<ContentPage {Ns}>\n" +
            "    <VerticalStackLayout>\n" +
            "        <Label AutomationId=\"dup\" />\n" +
            "        <Label AutomationId=\"dup\" />\n" +
            "    </VerticalStackLayout>\n" +
            "</ContentPage>\n";
        var map = XamlSourceMap.Parse(xaml, "f.xaml")!;

        Assert.True(map.TryGet("0/0", out var a));
        Assert.True(map.TryGet("0/1", out var b));
        Assert.Null(a.AutomationId); // duplicate among siblings → not used as identity
        Assert.Null(b.AutomationId);
    }

    [Fact]
    public void Parse_ContentHash_ChangesWithContent()
    {
        var xaml = $"<ContentPage {Ns}><Label /></ContentPage>";
        var h1 = XamlSourceMap.Parse(xaml, "f.xaml")!.ContentHash;
        var h2 = XamlSourceMap.Parse(xaml + " ", "f.xaml")!.ContentHash;
        Assert.NotEqual(h1, h2);
    }

    [Theory]
    [InlineData("<Label Text=\"Hello\" />", "Hello")]
    [InlineData("<Label Text=\"Line one&#10;line two\" />", "Line one\nline two")]
    [InlineData("<Label Text=\"{Binding Name}\" />", null)]
    [InlineData("<Label Text=\" {StaticResource Name}\" />", null)]
    [InlineData("<Label Text=\"{}{escaped}\" />", null)]
    [InlineData("<Label />", null)]
    public void Parse_ExtractsLiteralText_ButNotMarkupExtensions(string element, string? expected)
    {
        var map = XamlSourceMap.Parse($"<ContentPage {Ns}>{element}</ContentPage>", "f.xaml")!;

        Assert.True(map.TryGet("0", out var label));
        Assert.Equal(expected, label.Text);
    }

    // ── walker: reorder closed for named siblings ──

    [Fact]
    public void Reorder_OfAutomationIdSiblings_ResolvesToNull_NotWrongLine()
    {
        var map = MapWith("MainPage.xaml", "hash1",
            ("", Entry(1, "ContentPage", 1)),
            ("0", Entry(3, "VerticalStackLayout", 2)),
            ("0/0", EntryId(4, "Label", "A")),
            ("0/1", EntryId(5, "Label", "B")));

        // Runtime has the two labels SWAPPED (B at 0/0, A at 0/1).
        var root = IdNode("MyApp.MainPage", "ContentPage", "MyApp.MainPage", null,
            IdNode("vsl", "VerticalStackLayout", "Microsoft.Maui.Controls.VerticalStackLayout", null,
                IdNode("lblB", "Label", "Microsoft.Maui.Controls.Label", "B"),
                IdNode("lblA", "Label", "Microsoft.Maui.Controls.Label", "A")));

        WalkerWith("MyApp.MainPage", map).ApplySourceMap(new[] { root });

        Assert.Equal(1, root.SourceLine);                    // ContentPage maps
        Assert.Equal("hash1", root.SourceHash);              // hash attached
        Assert.Equal(3, root.Children![0].SourceLine);       // VSL maps (type match, count ok)
        Assert.Null(root.Children[0].Children![0].SourceLine); // swapped label → null, NOT line 4
        Assert.Null(root.Children[0].Children![1].SourceLine);  // swapped label → null, NOT line 5
    }

    [Fact]
    public void UnnamedSameTypeSiblings_WithoutLiteralText_AreWithheldBecauseOrderCannotBeVerified()
    {
        var map = MapWith("MainPage.xaml", "hash1",
            ("", Entry(1, "ContentPage", 1)),
            ("0", Entry(3, "VerticalStackLayout", 2)),
            ("0/0", Entry(4, "Label", 0)),
            ("0/1", Entry(5, "Label", 0)));

        var root = IdNode("MyApp.MainPage", "ContentPage", "MyApp.MainPage", null,
            IdNode("vsl", "VerticalStackLayout", "Microsoft.Maui.Controls.VerticalStackLayout", null,
                IdNode("second", "Label", "Microsoft.Maui.Controls.Label", null),
                IdNode("first", "Label", "Microsoft.Maui.Controls.Label", null)));

        WalkerWith("MyApp.MainPage", map).ApplySourceMap(new[] { root });

        Assert.Null(root.Children![0].Children![0].SourceLine);
        Assert.Null(root.Children[0].Children![1].SourceLine);
    }

    [Fact]
    public void UnnamedSameTypeSiblings_DoNotHideUniquelyTypedSibling()
    {
        var map = MapWith("MainPage.xaml", "hash1",
            ("", Entry(1, "ContentPage", 1)),
            ("0", Entry(3, "VerticalStackLayout", 3)),
            ("0/0", Entry(4, "Label", 0)),
            ("0/1", Entry(5, "Label", 0)),
            ("0/2", Entry(6, "Button", 0)));

        var root = IdNode("MyApp.MainPage", "ContentPage", "MyApp.MainPage", null,
            IdNode("vsl", "VerticalStackLayout", "Microsoft.Maui.Controls.VerticalStackLayout", null,
                IdNode("first", "Label", "Microsoft.Maui.Controls.Label", null),
                IdNode("second", "Label", "Microsoft.Maui.Controls.Label", null),
                IdNode("action", "Button", "Microsoft.Maui.Controls.Button", null)));

        WalkerWith("MyApp.MainPage", map).ApplySourceMap(new[] { root });

        Assert.Null(root.Children![0].Children![0].SourceLine);
        Assert.Null(root.Children[0].Children![1].SourceLine);
        Assert.Equal(6, root.Children[0].Children![2].SourceLine);
    }

    // ── walker: same-type siblings told apart by a verified literal Text ──

    [Fact]
    public void DefaultTemplate_UnnamedLabels_MapWhenTextMatchesRuntime()
    {
        const string xaml = """
            <ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
                         x:Class="MyApp.MainPage">
              <VerticalStackLayout>
                <Image Source="dotnet_bot.png" />
                <Label Text="Hello, World!" />
                <Label Text="Welcome to &#10;.NET Multi-platform App UI" />
                <Button x:Name="CounterBtn" Text="Click me" />
              </VerticalStackLayout>
            </ContentPage>
            """;
        var map = XamlSourceMap.Parse(xaml, "MainPage.xaml")!;

        var root = IdNode("MyApp.MainPage", "ContentPage", "MyApp.MainPage", null,
            IdNode("vsl", "VerticalStackLayout", "Microsoft.Maui.Controls.VerticalStackLayout", null,
                IdNode("image", "Image", "Microsoft.Maui.Controls.Image", null),
                TextNode("headline", "Label", "Hello, World!"),
                TextNode("subtitle", "Label", "Welcome to \n.NET Multi-platform App UI"),
                TextNode("counter", "Button", "Clicked 3 times"))); // runtime text may differ for a uniquely typed element

        WalkerWith("MyApp.MainPage", map).ApplySourceMap(new[] { root });

        var children = root.Children![0].Children!;
        Assert.Equal(new int?[] { 5, 6, 7, 8 }, children.Select(child => child.SourceLine));
    }

    [Fact]
    public void SameTypeSiblings_ReorderedAtRuntime_ResolveToNull_NotWrongLine()
    {
        var map = LabelSiblingsMap(("First", 4), ("Second", 5));

        var root = LabelSiblingsRoot("Second", "First");

        WalkerWith("MyApp.MainPage", map).ApplySourceMap(new[] { root });

        Assert.Equal(new int?[] { null, null }, LabelLines(root));
    }

    [Fact]
    public void SameTypeSiblings_WithChangedRuntimeText_UnmapOnlyTheChangedOne()
    {
        var map = LabelSiblingsMap(("Clicks: 0", 4), ("Static", 5));

        var root = LabelSiblingsRoot("Clicks: 3", "Static");

        WalkerWith("MyApp.MainPage", map).ApplySourceMap(new[] { root });

        Assert.Equal(new int?[] { null, 5 }, LabelLines(root));
    }

    [Theory]
    [InlineData("Edited")]
    [InlineData("Second")]
    public void AgentTextEdit_ReorderedSiblings_DoNotClaimAnotherDeclaration(string text)
    {
        var walker = WalkerWith("MyApp.MainPage", LabelSiblingsMap(("First", 4), ("Second", 5)));
        var labels = new[] { new Label { Text = "First" }, new Label { Text = "Second" } };
        RetainTextEdit(walker, labels, text);

        Assert.Equal(new int?[] { 4, 5 }, MappedLabelLines(walker, labels));
        Assert.Equal(new int?[] { null, null }, MappedLabelLines(walker, labels[1], labels[0]));
        Assert.Equal(new int?[] { 4, 5 }, MappedLabelLines(walker, labels));
    }

    [Fact]
    public void AgentTextEdit_UnobservedRuntimeText_IsNotTrusted()
    {
        var walker = WalkerWith("MyApp.MainPage", LabelSiblingsMap(("First", 4), ("Second", 5)));
        var labels = new[] { new Label { Text = "First" }, new Label { Text = "Second" } };
        RetainTextEdit(walker, labels, "Edited");

        labels[0].Text = "Changed by code-behind";
        Assert.Equal(new int?[] { null, 5 }, MappedLabelLines(walker, labels));
        labels[0].Text = "First";
        Assert.Equal(new int?[] { 4, 5 }, MappedLabelLines(walker, labels));
    }

    [Fact]
    public void AgentTextEdit_NewObjectWithReusedExternalId_DoesNotInheritSource()
    {
        var walker = WalkerWith("MyApp.MainPage", LabelSiblingsMap(("First", 4), ("Second", 5)));
        var labels = new[] { new Label { Text = "First" }, new Label { Text = "Second" } };
        RetainTextEdit(walker, labels, "Edited");

        Assert.Equal(new int?[] { null, 5 },
            MappedLabelLines(walker, new Label { Text = "Edited" }, labels[1]));
    }

    [Fact]
    public void AgentTextEdit_ExtraRuntimeSibling_RemainsAmbiguous()
    {
        var walker = WalkerWith("MyApp.MainPage", LabelSiblingsMap(("First", 4), ("Second", 5)));
        var labels = new[] { new Label { Text = "First" }, new Label { Text = "Second" } };
        RetainTextEdit(walker, labels, "Edited");

        Assert.Equal(new int?[] { null, null, null },
            MappedLabelLines(walker, labels[0], labels[1], new Label { Text = "Added" }));
    }

    [Theory]
    [InlineData("OtherPage.xaml", "h", 4, 1)]
    [InlineData("MainPage.xaml", "other-hash", 4, 1)]
    [InlineData("MainPage.xaml", "h", 40, 1)]
    [InlineData("MainPage.xaml", "h", 4, 2)]
    public void AgentTextEdit_ChangedSourceMap_DoesNotReuseSource(string file, string hash, int line, int column)
    {
        var provider = new TestMapProvider();
        provider.Add("MyApp.MainPage", LabelSiblingsMap(("First", 4), ("Second", 5)));
        var walker = new VisualTreeWalker { SourceMapProvider = provider };
        var labels = new[] { new Label { Text = "First" }, new Label { Text = "Second" } };
        RetainTextEdit(walker, labels, "Edited");
        provider.Add("MyApp.MainPage", MapWith(file, hash,
            ("", Entry(1, "ContentPage", 1)),
            ("0", Entry(3, "VerticalStackLayout", 2)),
            ("0/0", new XamlSourceEntry(line, column, "Label", 0) { Text = "First" }),
            ("0/1", new XamlSourceEntry(5, 1, "Label", 0) { Text = "Second" })));

        Assert.Equal(new int?[] { null, 5 }, MappedLabelLines(walker, labels));
    }

    [Fact]
    public void SameTypeSiblings_SharingLiteralText_AreWithheld()
    {
        var map = LabelSiblingsMap(("Same", 4), ("Other", 5), ("Same", 6));

        var root = LabelSiblingsRoot("Same", "Other", "Same");

        WalkerWith("MyApp.MainPage", map).ApplySourceMap(new[] { root });

        Assert.Equal(new int?[] { null, 5, null }, LabelLines(root));
    }

    [Fact]
    public void SameTypeSiblings_WithBoundSibling_AreWithheld()
    {
        // The bound sibling can show any text at runtime, including the literal one.
        var map = LabelSiblingsMap(("Total", 4), (null, 5));

        var root = LabelSiblingsRoot("Total", "Total");

        WalkerWith("MyApp.MainPage", map).ApplySourceMap(new[] { root });

        Assert.Equal(new int?[] { null, null }, LabelLines(root));
    }

    [Fact]
    public void SameTypeSiblings_WithBoundSiblingInAnotherNamespaceSpelling_AreWithheld()
    {
        // <c:Label> resolves to a full type name and <Label> does not, yet both are a MAUI Label.
        const string xaml = """
            <ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
                         xmlns:c="clr-namespace:Microsoft.Maui.Controls;assembly=Microsoft.Maui.Controls">
              <VerticalStackLayout>
                <Label Text="A" />
                <Label Text="B" />
                <c:Label Text="{Binding Title}" />
              </VerticalStackLayout>
            </ContentPage>
            """;
        var map = XamlSourceMap.Parse(xaml, "MainPage.xaml")!;

        // Title shows "A" and code-behind moved the bound label to the front.
        var root = LabelSiblingsRoot("A", "B", "A");

        WalkerWith("MyApp.MainPage", map).ApplySourceMap(new[] { root });

        Assert.Equal(new int?[] { null, null, null }, LabelLines(root));
    }

    [Fact]
    public void SameTypeSiblings_WithExtraRuntimeSibling_AreWithheld()
    {
        var map = LabelSiblingsMap(("First", 4), ("Second", 5));

        var root = LabelSiblingsRoot("First", "Second", "Added in code-behind");

        WalkerWith("MyApp.MainPage", map).ApplySourceMap(new[] { root });

        Assert.Equal(new int?[] { null, null, null }, LabelLines(root));
    }

    [Fact]
    public void WalkTree_RealLabels_MapOnlyWhileRuntimeTextsMatchTheXaml()
    {
        const string xaml = """
            <ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui">
              <VerticalStackLayout>
                <Label Text="First" />
                <Label Text="Second" />
              </VerticalStackLayout>
            </ContentPage>
            """;
        var provider = new TestMapProvider();
        provider.Add(typeof(SiblingsPage).FullName!, XamlSourceMap.Parse(xaml, "SiblingsPage.xaml")!);

        Assert.Equal(
            new int?[] { 3, 4 },
            WalkSiblingLines(provider, "Label", new Label { Text = "First" }, new Label { Text = "Second" }));
        Assert.Equal(
            new int?[] { null, null },
            WalkSiblingLines(provider, "Label", new Label { Text = "Second" }, new Label { Text = "First" }));
    }

    [Fact]
    public void WalkTree_RealEntries_RedactedPasswordTextDoesNotIdentifyASibling()
    {
        // A password Entry reports the redaction marker, so it looks like an Entry that literally shows it.
        const string xaml = """
            <ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui">
              <VerticalStackLayout>
                <Entry Text="[REDACTED]" />
                <Entry Text="secret" IsPassword="True" />
              </VerticalStackLayout>
            </ContentPage>
            """;
        var provider = new TestMapProvider();
        provider.Add(typeof(SiblingsPage).FullName!, XamlSourceMap.Parse(xaml, "SiblingsPage.xaml")!);

        // Code-behind put the password Entry first.
        Assert.Equal(
            new int?[] { null, null },
            WalkSiblingLines(
                provider,
                "Entry",
                new Entry { Text = "secret", IsPassword = true },
                new Entry { Text = "[REDACTED]" }));
    }

    [Fact]
    public void InOrder_AutomationIdSiblings_MapCorrectly()
    {
        var map = MapWith("MainPage.xaml", "h",
            ("", Entry(1, "ContentPage", 1)),
            ("0", Entry(3, "VerticalStackLayout", 2)),
            ("0/0", EntryId(4, "Label", "A")),
            ("0/1", EntryId(5, "Label", "B")));

        var root = IdNode("MyApp.MainPage", "ContentPage", "MyApp.MainPage", null,
            IdNode("vsl", "VerticalStackLayout", "Microsoft.Maui.Controls.VerticalStackLayout", null,
                IdNode("lblA", "Label", "Microsoft.Maui.Controls.Label", "A"),
                IdNode("lblB", "Label", "Microsoft.Maui.Controls.Label", "B")));

        WalkerWith("MyApp.MainPage", map).ApplySourceMap(new[] { root });

        Assert.Equal(4, root.Children![0].Children![0].SourceLine);
        Assert.Equal(5, root.Children[0].Children![1].SourceLine);
    }

    // ── walker: full CLR type closes clr-namespace collisions ──

    [Fact]
    public void FullType_Mismatch_AtResolvedPath_ResolvesToNull()
    {
        var map = MapWith("Page.xaml", "h",
            ("", Entry(1, "ContentPage", 1)),
            ("0", Entry(2, "Card", 0, fullType: "MyApp.Controls.Card")));

        var correct = IdNode("MyApp.Page", "ContentPage", "MyApp.Page", null,
            IdNode("c", "Card", "MyApp.Controls.Card", null));
        WalkerWith("MyApp.Page", map).ApplySourceMap(new[] { correct });
        Assert.Equal(2, correct.Children![0].SourceLine); // exact CLR type → maps

        var collision = IdNode("MyApp.Page", "ContentPage", "MyApp.Page", null,
            IdNode("c", "Card", "OtherLib.Card", null)); // same short name, different assembly
        WalkerWith("MyApp.Page", map).ApplySourceMap(new[] { collision });
        Assert.Null(collision.Children![0].SourceLine);   // FullType mismatch → null, not wrong
    }

    // ── helpers ──

    private static ElementInfo IdNode(string id, string type, string fullType, string? automationId, params ElementInfo[] children) => new()
    {
        Id = id,
        Type = type,
        FullType = fullType,
        AutomationId = automationId,
        Children = children.Length > 0 ? new List<ElementInfo>(children) : null,
    };

    private static XamlSourceEntry Entry(int line, string type, int childCount, string? fullType = null)
        => new(line, 1, type, childCount, null, fullType);

    private static XamlSourceEntry EntryId(int line, string type, string automationId, int childCount = 0)
        => new(line, 1, type, childCount, automationId, null);

    private static ElementInfo TextNode(string id, string type, string? text) => new()
    {
        Id = id,
        Type = type,
        FullType = $"Microsoft.Maui.Controls.{type}",
        Text = text,
    };

    // A page with one VerticalStackLayout holding unnamed Labels declared with these literal texts
    // (null = bound or missing) at these XAML lines.
    private static XamlSourceMap LabelSiblingsMap(params (string? Text, int Line)[] labels)
    {
        var entries = new List<(string Path, XamlSourceEntry Entry)>
        {
            ("", Entry(1, "ContentPage", 1)),
            ("0", Entry(3, "VerticalStackLayout", labels.Length)),
        };
        for (var index = 0; index < labels.Length; index++)
            entries.Add(($"0/{index}", new XamlSourceEntry(labels[index].Line, 1, "Label", 0) { Text = labels[index].Text }));
        return MapWith("MainPage.xaml", "h", entries.ToArray());
    }

    private static ElementInfo LabelSiblingsRoot(params string?[] runtimeTexts)
        => IdNode("MyApp.MainPage", "ContentPage", "MyApp.MainPage", null,
            IdNode("vsl", "VerticalStackLayout", "Microsoft.Maui.Controls.VerticalStackLayout", null,
                runtimeTexts.Select((text, index) => TextNode($"label{index}", "Label", text)).ToArray()));

    private static ElementInfo LabelSiblingsRoot(params Label[] labels)
    {
        var root = LabelSiblingsRoot(labels.Select(label => label.Text).ToArray());
        for (var index = 0; index < labels.Length; index++)
            root.Children![0].Children![index].IdentityToken = labels[index];
        return root;
    }

    private static void RetainTextEdit(VisualTreeWalker walker, Label[] labels, string text)
    {
        var before = LabelSiblingsRoot(labels);
        walker.ApplySourceMap(new[] { before });
        Assert.Equal(new int?[] { 4, 5 }, LabelLines(before));
        labels[0].Text = text;
        walker.RetainSourceForTextEdit(labels[0], before.Children![0].Children![0]);
    }

    private static int?[] MappedLabelLines(VisualTreeWalker walker, params Label[] labels)
    {
        var root = LabelSiblingsRoot(labels);
        walker.ApplySourceMap(new[] { root });
        return LabelLines(root);
    }

    private static int?[] LabelLines(ElementInfo root)
        => root.Children![0].Children!.Select(label => label.SourceLine).ToArray();

    private sealed class SiblingsPage : ContentPage
    {
        public SiblingsPage(View[] siblings)
        {
            var layout = new VerticalStackLayout();
            foreach (var sibling in siblings)
                layout.Children.Add(sibling);
            Content = layout;
        }
    }

    // Walks real controls, so the runtime text is exactly what ExtractText reports in production.
    private static int?[] WalkSiblingLines(IXamlSourceMapProvider provider, string type, params View[] siblings)
    {
        var app = new Application();
        typeof(Application)
            .GetMethod("AddWindow", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(app, [new Window(new SiblingsPage(siblings))]);

        var tree = new VisualTreeWalker { SourceMapProvider = provider }.WalkTree(app);

        return VisualTreeWalker.FlattenElementInfos(tree)
            .Where(element => element.Type == type)
            .Select(element => element.SourceLine)
            .ToArray();
    }

    private static XamlSourceMap MapWith(string file, string? hash, params (string Path, XamlSourceEntry Entry)[] entries)
        => new(file, entries.ToDictionary(e => e.Path, e => e.Entry, StringComparer.Ordinal), hash);

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
