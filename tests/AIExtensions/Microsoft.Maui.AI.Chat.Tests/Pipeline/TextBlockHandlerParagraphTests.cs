// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Maui.AI.Chat.Tests.Pipeline;

public class TextBlockHandlerParagraphTests
{
    [Fact]
    public void RebuildParagraphs_TwoParagraphs_ProducesParagraphTextNodes()
    {
        var block = new TextContentBlock();
        block.AppendText("First\n\nSecond");

        TextBlockHandler.RebuildParagraphs(block);

        Assert.Equal(2, block.Content.Count);
        AssertParagraph(block.Content[0], "First");
        AssertParagraph(block.Content[1], "Second");
    }

    [Fact]
    public void RebuildParagraphs_SingleNewline_RemainsOneParagraph()
    {
        var block = new TextContentBlock();
        block.AppendText("Line one\nLine two");

        TextBlockHandler.RebuildParagraphs(block);

        AssertParagraph(Assert.Single(block.Content), "Line one\nLine two");
    }

    [Fact]
    public void RebuildParagraphs_TrailingBreak_DoesNotCreateEmptyParagraph()
    {
        var block = new TextContentBlock();
        block.AppendText("Hello\n\n");

        TextBlockHandler.RebuildParagraphs(block);

        AssertParagraph(Assert.Single(block.Content), "Hello");
    }

    [Fact]
    public void RebuildParagraphs_OnlyBreaks_ProducesNoContent()
    {
        var block = new TextContentBlock();
        block.AppendText("\n\n\n\n");

        TextBlockHandler.RebuildParagraphs(block);

        Assert.Empty(block.Content);
    }

    [Fact]
    public void StreamingText_RebuildsProjectionAsTextArrives()
    {
        var block = new TextContentBlock();
        block.AppendText("Hello");
        TextBlockHandler.RebuildParagraphs(block);
        block.AppendText(" world\n\nSecond");
        TextBlockHandler.RebuildParagraphs(block);

        Assert.Equal(2, block.Content.Count);
        AssertParagraph(block.Content[0], "Hello world");
        AssertParagraph(block.Content[1], "Second");
    }

    private static void AssertParagraph(RichTextNode node, string expected)
    {
        var paragraph = Assert.IsType<ParagraphNode>(node);
        Assert.Equal(
            expected,
            Assert.IsType<TextNode>(Assert.Single(paragraph.Children)).Text);
    }
}
