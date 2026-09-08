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
    public void UnnamedSameTypeSiblings_AreWithheldBecauseOrderCannotBeVerified()
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
