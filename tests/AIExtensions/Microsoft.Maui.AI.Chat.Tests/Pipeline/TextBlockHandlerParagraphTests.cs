// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Maui.AI.Chat.Tests.Pipeline;

public class TextBlockHandlerParagraphTests
{
    [Fact]
    public void Paragraphs_TwoParagraphs_ProducesParagraphTextNodes()
    {
        var block = new TextContentBlock();
        block.AppendText("First\n\nSecond");

        Assert.Equal(2, block.Content.Count);
        AssertParagraph(block.Content[0], "First");
        AssertParagraph(block.Content[1], "Second");
    }

    [Fact]
    public void Paragraphs_SingleNewline_RemainsOneParagraph()
    {
        var block = new TextContentBlock();
        block.AppendText("Line one\nLine two");

        AssertParagraph(Assert.Single(block.Content), "Line one\nLine two");
    }

    [Fact]
    public void Paragraphs_TrailingBreak_DoesNotCreateEmptyParagraph()
    {
        var block = new TextContentBlock();
        block.AppendText("Hello\n\n");

        AssertParagraph(Assert.Single(block.Content), "Hello");
    }

    [Fact]
    public void Paragraphs_OnlyBreaks_ProducesNoContent()
    {
        var block = new TextContentBlock();
        block.AppendText("\n\n\n\n");

        Assert.Empty(block.Content);
    }

    [Fact]
    public void StreamingText_UpdatesProjectionAsTextArrives()
    {
        var block = new TextContentBlock();
        block.AppendText("Hello");
        Assert.Single(block.Content);
        block.AppendText(" world\n\nSecond");

        Assert.Equal(2, block.Content.Count);
        AssertParagraph(block.Content[0], "Hello world");
        AssertParagraph(block.Content[1], "Second");
    }

    [Fact]
    public void Paragraphs_BlankLineSplitAcrossSegments_SplitsParagraphs()
    {
        var block = new TextContentBlock();
        block.AppendText("First\n");
        Assert.Single(block.Content);

        block.AppendText("\nSecond");

        Assert.Collection(
            block.Content,
            node => AssertParagraph(node, "First"),
            node => AssertParagraph(node, "Second"));
    }

    [Fact]
    public void Paragraphs_CompletedParagraphsKeepTheirNodesWhileTextStreams()
    {
        var block = new TextContentBlock();
        block.AppendText("First\n\nSec");
        var first = block.Content[0];
        Assert.Same(block.Content, block.Content);

        block.AppendText("ond");

        Assert.Same(first, block.Content[0]);
        AssertParagraph(block.Content[1], "Second");
    }

    [Theory]
    [InlineData("a\n\nb")]
    [InlineData("a\n\n\nb\n\n")]
    [InlineData("\n\na\r\n\r\nb\nc\n\n\n\nd")]
    [InlineData("line one\nline two\n\n\n\nend\n")]
    public void Paragraphs_ReadWhileStreaming_MatchReadingOnce(string text)
    {
        var expectedTexts = SplitParagraphs(text);

        for (var first = 0; first <= text.Length; first++)
        {
            for (var second = first; second <= text.Length; second++)
            {
                var streamed = new TextContentBlock();
                streamed.AppendText(text[..first]);
                _ = streamed.Content;
                streamed.AppendText(text[first..second]);
                _ = streamed.Content;
                streamed.AppendText(text[second..]);

                Assert.Equal(expectedTexts, GetParagraphTexts(streamed));
            }
        }
    }

    [Fact]
    public void ProvidedContent_WinsOverParagraphs()
    {
        var block = new RichContentBlock();
        IReadOnlyList<RichTextNode> provided = [new HeadingNode(1)];
        block.ReplaceContent("# Title", provided);

        block.AppendText("\n\nMore");

        Assert.Same(provided, block.Content);
        Assert.Equal("# Title\n\nMore", block.RawText);
    }

    // The projection the text handler used to rebuild from the whole text on every token.
    private static List<string> SplitParagraphs(string text)
    {
        var paragraphs = new List<string>();
        var start = 0;
        while (start < text.Length)
        {
            var breakIndex = text.IndexOf("\n\n", start, StringComparison.Ordinal);
            var end = breakIndex < 0 ? text.Length : breakIndex;
            var paragraph = text[start..end].TrimEnd('\r', '\n');
            if (paragraph.Length > 0)
                paragraphs.Add(paragraph);
            if (breakIndex < 0)
                break;
            start = breakIndex + 2;
        }

        return paragraphs;
    }

    private static List<string> GetParagraphTexts(RichContentBlock block) =>
        block.Content
            .Select(node => Assert.IsType<TextNode>(Assert.Single(Assert.IsType<ParagraphNode>(node).Children)).Text)
            .ToList();

    private static void AssertParagraph(RichTextNode node, string expected)
    {
        var paragraph = Assert.IsType<ParagraphNode>(node);
        Assert.Equal(
            expected,
            Assert.IsType<TextNode>(Assert.Single(paragraph.Children)).Text);
    }
}
