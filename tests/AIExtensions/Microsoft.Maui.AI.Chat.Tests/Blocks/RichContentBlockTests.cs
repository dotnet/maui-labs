// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Maui.AI.Chat.Tests.Blocks;

public class RichContentBlockTests
{
    [Fact]
    public void AppendText_AccumulatesAndInvalidatesCache()
    {
        var block = new RichContentBlock();
        block.AppendText("Hello");
        var first = block.RawText;

        block.AppendText(" world");
        var second = block.RawText;

        Assert.Equal("Hello world", second);
        Assert.NotSame(first, second);
        Assert.Same(second, block.RawText);
    }

    [Fact]
    public void Content_DefaultsEmpty()
    {
        var block = new RichContentBlock();

        Assert.Empty(block.Content);
    }

    [Fact]
    public void TextContentBlock_IsRichContentCompatibilityType()
    {
        var block = new TextContentBlock();
        block.AppendText("Hello");

        var rich = Assert.IsAssignableFrom<RichContentBlock>(block);
        Assert.Equal("Hello", rich.RawText);
    }

    [Fact]
    public void AppendText_Null_Throws()
    {
        var block = new RichContentBlock();

        Assert.Throws<ArgumentNullException>(() => block.AppendText(null!));
    }

    [Fact]
    public void ReplaceContent_ReplacesTextAndNodes()
    {
        var block = new RichContentBlock();
        block.AppendText("old");
        IReadOnlyList<RichTextNode> nodes =
        [
            new HeadingNode(2)
            {
            },
        ];

        block.ReplaceContent("new", nodes);

        Assert.Equal("new", block.RawText);
        Assert.Same(nodes, block.Content);
    }

    [Fact]
    public void ReplaceContent_NullArguments_Throw()
    {
        var block = new RichContentBlock();

        Assert.Throws<ArgumentNullException>(() =>
            block.ReplaceContent(null!, Array.Empty<RichTextNode>()));
        Assert.Throws<ArgumentNullException>(() =>
            block.ReplaceContent("", null!));
    }
}
