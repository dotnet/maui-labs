using Microsoft.Maui.DevFlow.Agent.Core;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// Verifies the id-based source transfer used by the element-detail endpoint: the depth-limited
/// detail subtree receives source from a full source-mapped walk, matched by element id, and
/// unmatched nodes stay null.
/// </summary>
public class ElementDetailSourceTransferTests
{
    private static ElementInfo Node(string id, string? file = null, int? line = null, int? col = null, string? hash = null, params ElementInfo[] children) => new()
    {
        Id = id,
        SourceFile = file,
        SourceLine = line,
        SourceColumn = col,
        SourceHash = hash,
        Children = children.Length > 0 ? new List<ElementInfo>(children) : null,
    };

    [Fact]
    public void CollectSourceById_GathersOnlyNodesWithSource()
    {
        var tree = Node("root", "Page.xaml", 1, 1, "h0",
            Node("a", "Page.xaml", 3, 5, "hA"),
            Node("b"));

        var map = VisualTreeWalker.CollectSourceById(new[] { tree });

        Assert.Equal(2, map.Count);
        Assert.Equal(("Page.xaml", 3, 5, "hA"), map["a"]);
        Assert.True(map.ContainsKey("root"));
        Assert.False(map.ContainsKey("b"));
    }

    [Fact]
    public void ApplySourceById_TransfersByIdOntoDetailAndChildren()
    {
        var sources = new Dictionary<string, (string File, int Line, int Column, string? Hash)>
        {
            ["target"] = ("Detail.xaml", 10, 2, "hT"),
            ["child"] = ("Detail.xaml", 12, 6, "hC"),
        };

        var detail = Node("target", null, null, null, null,
            Node("child"),
            Node("unmapped"));

        VisualTreeWalker.ApplySourceById(detail, sources);

        Assert.Equal("Detail.xaml", detail.SourceFile);
        Assert.Equal(10, detail.SourceLine);
        Assert.Equal(2, detail.SourceColumn);
        Assert.Equal("hT", detail.SourceHash);
        Assert.Equal(12, detail.Children![0].SourceLine);
        Assert.Equal("hC", detail.Children![0].SourceHash);
        Assert.Null(detail.Children![1].SourceFile);
    }

    [Fact]
    public void ApplySourceById_NoMatch_LeavesNull()
    {
        var detail = Node("x");
        VisualTreeWalker.ApplySourceById(detail, new Dictionary<string, (string File, int Line, int Column, string? Hash)>());
        Assert.Null(detail.SourceFile);
    }
}
