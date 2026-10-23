// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Maui.AI.Chat.Tests.Blocks;

public class RichTextNodeTests
{
    [Fact]
    public void AddChild_BuildsNestedTree()
    {
        var paragraph = new ParagraphNode();
        paragraph.AddChild(new TextNode("Hello "));
        var emphasis = new EmphasisNode();
        emphasis.AddChild(new TextNode("world"));
        paragraph.AddChild(emphasis);

        Assert.Equal(2, paragraph.Children.Count);
        Assert.Equal("Hello ", Assert.IsType<TextNode>(paragraph.Children[0]).Text);
        Assert.Equal(
            "world",
            Assert.IsType<TextNode>(
                Assert.Single(Assert.IsType<EmphasisNode>(paragraph.Children[1]).Children)).Text);
    }

    [Fact]
    public void AddChild_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ParagraphNode().AddChild(null!));
    }

    [Fact]
    public void CommonNodes_PreserveConfiguredValues()
    {
        var heading = new HeadingNode(3);
        var code = new CodeBlockNode("var x = 1;", "csharp");
        var link = new LinkNode("https://example.com", "Example");
        var image = new ImageNode("https://example.com/image.png", "alt", "title");
        var list = new ListNode(ordered: true, start: 5);

        Assert.Equal(3, heading.Level);
        Assert.Equal("var x = 1;", code.Code);
        Assert.Equal("csharp", code.Language);
        Assert.Equal("https://example.com", link.Url);
        Assert.Equal("Example", link.Title);
        Assert.Equal("alt", image.Alt);
        Assert.Equal("title", image.Title);
        Assert.True(list.Ordered);
        Assert.Equal(5, list.Start);
    }

    [Fact]
    public void ReferenceAndTableNodes_PreserveConfiguredValues()
    {
        var reference = new LinkReferenceNode
        {
            Label = "docs",
            ReferenceKind = ReferenceKind.Full,
        };
        var table = new TableNode
        {
            Alignment =
            [
                TableColumnAlignment.Left,
                TableColumnAlignment.Right,
            ],
        };

        Assert.Equal("docs", reference.Label);
        Assert.Equal(ReferenceKind.Full, reference.ReferenceKind);
        Assert.Equal(
            [TableColumnAlignment.Left, TableColumnAlignment.Right],
            table.Alignment);
    }
}
