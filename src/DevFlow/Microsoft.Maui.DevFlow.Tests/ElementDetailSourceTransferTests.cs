using Microsoft.Maui.DevFlow.Agent.Core;

namespace Microsoft.Maui.DevFlow.Tests;

public class ElementDetailSourceTransferTests
{
    private static ElementInfo Node(
        string id,
        string? file = null,
        int? line = null,
        int? column = null,
        params ElementInfo[] children)
        => new()
        {
            Id = id,
            SourceFile = file,
            SourceLine = line,
            SourceColumn = column,
            Children = children.Length > 0 ? children.ToList() : null
        };

    [Fact]
    public void CollectSourceById_GathersOnlyNodesWithSource()
    {
        var tree = Node(
            "root",
            "Page.xaml",
            1,
            1,
            Node("a", "Page.xaml", 3, 5),
            Node("b"));

        var map = VisualTreeWalker.CollectSourceById([tree]);

        Assert.Equal(2, map.Count);
        Assert.Equal(("Page.xaml", 3, 5), map["a"]);
        Assert.False(map.ContainsKey("b"));
    }

    [Fact]
    public void ApplySourceById_TransfersSourceToDetailSubtree()
    {
        var sources = new Dictionary<string, (string File, int Line, int Column)>
        {
            ["target"] = ("Detail.xaml", 10, 2),
            ["child"] = ("Detail.xaml", 12, 6)
        };
        var detail = Node("target", children:
        [
            Node("child"),
            Node("unmapped")
        ]);

        VisualTreeWalker.ApplySourceById(detail, sources);

        Assert.Equal("Detail.xaml", detail.SourceFile);
        Assert.Equal(10, detail.SourceLine);
        Assert.Equal(12, detail.Children![0].SourceLine);
        Assert.Null(detail.Children[1].SourceFile);
    }
}
