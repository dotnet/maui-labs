using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat;
using Microsoft.Maui.AI.Chat.Controls.Tests.TestHelpers;
using Microsoft.Maui.Chat.Controls;

namespace Microsoft.Maui.AI.Chat.Controls.Tests;

public class RichTextViewTests
{
    [Fact]
    public void Template_MatchesStructuredBlockButNotPlainTextCompatibilityBlock()
    {
        var template = new RichTextContentTemplate();
        var session = SessionFactory.Create("unused");
        var rich = new RichContentBlock { Id = "rich", Role = ChatRole.Assistant };
        var text = new TextContentBlock { Id = "text", Role = ChatRole.Assistant };
        rich.ReplaceContent("rich", [CreateParagraph("rich")]);
        var richContext = new ContentContext(session, rich);

        Assert.IsAssignableFrom<
            StructuredTextMessageContent<IReadOnlyList<RichTextNode>>>(
            richContext.Content);
        Assert.True(template.When(richContext));
        Assert.False(template.When(new ContentContext(session, text)));
    }

    [Fact]
    public void DefaultTemplate_UsesTheProviderNeutralMessageChrome()
    {
        var template = new RichTextContentTemplate();
        var view = template.GetTemplate().CreateContent();

        Assert.IsAssignableFrom<ChatBubbleView>(view);
    }

    [Theory]
    [InlineData("https://example.com", true)]
    [InlineData("http://example.com", true)]
    [InlineData("mailto:test@example.com", true)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("/relative", false)]
    [InlineData("", false)]
    public void IsSafeUri_AllowsOnlyExplicitExternalSchemes(
        string value,
        bool expected)
    {
        Assert.Equal(expected, RichTextView.IsSafeUri(value));
    }

    [Fact]
    public void StructuredNodes_RenderNativeViewsAndInlineFormatting()
    {
        var heading = new HeadingNode(2);
        heading.AddChild(new TextNode("Heading"));

        var paragraph = new ParagraphNode();
        paragraph.AddChild(new TextNode("plain "));
        var strong = new StrongNode();
        strong.AddChild(new TextNode("bold"));
        paragraph.AddChild(strong);
        var emphasis = new EmphasisNode();
        emphasis.AddChild(new TextNode(" italic"));
        paragraph.AddChild(emphasis);
        var strike = new StrikethroughNode();
        strike.AddChild(new TextNode(" removed"));
        paragraph.AddChild(strike);
        paragraph.AddChild(new InlineCodeNode("code"));
        var link = new LinkNode("https://example.com");
        link.AddChild(new TextNode(" link"));
        paragraph.AddChild(link);

        var quote = new BlockQuoteNode();
        quote.AddChild(CreateParagraph("quoted"));

        var list = new ListNode(ordered: true, start: 3);
        var listItem = new ListItemNode();
        listItem.AddChild(CreateParagraph("item"));
        list.AddChild(listItem);

        var table = new TableNode
        {
            Alignment = [TableColumnAlignment.Right],
        };
        var row = new TableRowNode();
        var cell = new TableCellNode();
        cell.AddChild(new TextNode("cell"));
        row.AddChild(cell);
        table.AddChild(row);

        var footnote = new FootnoteDefinitionNode { Label = "1" };
        footnote.AddChild(new TextNode("note"));

        var block = new RichContentBlock
        {
            Id = "rich",
            Role = ChatRole.Assistant,
        };
        block.ReplaceContent(
            "fallback",
            [
                heading,
                paragraph,
                new CodeBlockNode("var answer = 42;", "csharp"),
                quote,
                list,
                table,
                new ThematicBreakNode(),
                new ImageNode("javascript:bad", "unsafe image"),
                new HtmlNode("<b>escaped</b>"),
                footnote,
            ]);
        var view = new RichTextView();

        view.ApplyContentContext(new ContentContext(
            SessionFactory.Create("unused"),
            block));

        var document = Assert.IsType<VerticalStackLayout>(view.RenderedContent);
        Assert.Equal(10, document.Children.Count);
        Assert.IsType<Label>(document.Children[0]);
        var paragraphLabel = Assert.IsType<Label>(document.Children[1]);
        Assert.Contains(
            paragraphLabel.FormattedText.Spans,
            span => span.FontAttributes.HasFlag(FontAttributes.Bold));
        Assert.Contains(
            paragraphLabel.FormattedText.Spans,
            span => span.FontAttributes.HasFlag(FontAttributes.Italic));
        Assert.Contains(
            paragraphLabel.FormattedText.Spans,
            span => span.TextDecorations.HasFlag(TextDecorations.Strikethrough));
        Assert.Contains(
            paragraphLabel.FormattedText.Spans,
            span => span.FontFamily == "monospace");
        Assert.Contains(
            paragraphLabel.FormattedText.Spans,
            span => span.GestureRecognizers.Count == 1);
        Assert.IsType<Border>(document.Children[2]);
        Assert.IsType<Grid>(document.Children[3]);
        Assert.IsType<VerticalStackLayout>(document.Children[4]);
        Assert.IsType<ScrollView>(document.Children[5]);
        Assert.IsType<BoxView>(document.Children[6]);
        Assert.Equal(
            "unsafe image",
            Assert.IsType<Label>(document.Children[7]).Text);
        Assert.Equal(
            "<b>escaped</b>",
            Assert.IsType<Label>(document.Children[8]).Text);
        Assert.IsType<HorizontalStackLayout>(document.Children[9]);
    }

    [Fact]
    public void EmptyNodeTree_RendersRawText()
    {
        var block = new RichContentBlock
        {
            Id = "rich",
            Role = ChatRole.Assistant,
        };
        block.ReplaceContent("raw fallback", []);
        var view = new RichTextView();

        view.ApplyContentContext(new ContentContext(
            SessionFactory.Create("unused"),
            block));

        Assert.Equal(
            "raw fallback",
            Assert.IsType<Label>(view.RenderedContent).Text);
    }

    private static ParagraphNode CreateParagraph(string text)
    {
        var paragraph = new ParagraphNode();
        paragraph.AddChild(new TextNode(text));
        return paragraph;
    }
}
