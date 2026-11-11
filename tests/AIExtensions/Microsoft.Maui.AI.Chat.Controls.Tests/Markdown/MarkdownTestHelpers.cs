using System.Text;
using Microsoft.Maui.AI.Chat;

namespace Microsoft.Maui.AI.Chat.Controls.Tests.Markdown;

// Compact, deterministic rendering of a rich-text tree for assertions.
internal static class MarkdownTestHelpers
{
    public static string Dump(IReadOnlyList<RichTextNode> nodes)
    {
        var builder = new StringBuilder();
        AppendNodes(builder, nodes);
        return builder.ToString();
    }

    public static string Dump(string markdown) =>
        Dump(MarkdownRichTextParser.ParseWithoutFallback(markdown));

    // Text a reader would see, plus the parts of the source that live in node properties
    // (URLs, titles, alt text, code languages, list numbers and task checks).
    public static string GetVisibleText(IReadOnlyList<RichTextNode> nodes)
    {
        // A loop over pending work, not recursion: the parser tests map trees thousands of levels deep, and
        // the thread running a test may have a small stack.
        var builder = new StringBuilder();
        var pending = new Stack<object>();
        Push(pending, nodes);
        while (pending.Count > 0)
        {
            switch (pending.Pop())
            {
                case string text:
                    builder.Append(text);
                    break;
                case TextNode text:
                    builder.Append(text.Text);
                    break;
                case InlineCodeNode code:
                    builder.Append(code.Code);
                    break;
                case CodeBlockNode block:
                    builder.Append(block.Language).Append('\n').Append(block.Code).Append('\n');
                    break;
                case LineBreakNode:
                    builder.Append('\n');
                    break;
                case ImageNode image:
                    builder.Append(image.Alt).Append(' ').Append(image.Url).Append(' ').Append(image.Title);
                    break;
                case LinkNode link:
                    pending.Push(" " + link.Url + " " + link.Title);
                    Push(pending, link.Children);
                    break;
                case ListNode list:
                    var number = list.Start ?? 1;
                    var parts = new List<object>();
                    foreach (var item in list.Children)
                    {
                        if (list.Ordered)
                            parts.Add(number++ + ". ");
                        if (item is ListItemNode { Checked: true })
                            parts.Add("[x] ");
                        parts.AddRange(item.Children);
                        parts.Add("\n");
                    }
                    Push(pending, parts);
                    break;
                case RichTextNode node:
                    pending.Push(" ");
                    Push(pending, node.Children);
                    break;
            }
        }

        return builder.ToString();
    }

    public static ParagraphNode Paragraph(params RichTextNode[] children)
    {
        var paragraph = new ParagraphNode();
        foreach (var child in children)
            paragraph.AddChild(child);
        return paragraph;
    }

    public static ParagraphNode Paragraph(string text) => Paragraph(new TextNode(text));

    public static T With<T>(T node, params RichTextNode[] children)
        where T : RichTextNode
    {
        foreach (var child in children)
            node.AddChild(child);
        return node;
    }

    private static void AppendNodes(StringBuilder builder, IReadOnlyList<RichTextNode> nodes)
    {
        for (var i = 0; i < nodes.Count; i++)
        {
            if (i > 0)
                builder.Append(' ');
            AppendNode(builder, nodes[i]);
        }
    }

    private static void AppendNode(StringBuilder builder, RichTextNode node)
    {
        switch (node)
        {
            case TextNode text:
                builder.Append('"').Append(text.Text).Append('"');
                return;
            case InlineCodeNode code:
                builder.Append("code\"").Append(code.Code).Append('"');
                return;
            case LineBreakNode:
                builder.Append("br");
                return;
            case ThematicBreakNode:
                builder.Append("hr");
                return;
            case CodeBlockNode block:
                builder.Append("pre");
                if (block.Language is not null)
                    builder.Append('[').Append(block.Language).Append(']');
                builder.Append('"').Append(block.Code).Append('"');
                return;
            case ImageNode image:
                builder.Append("img[").Append(image.Url).Append('|').Append(image.Alt);
                if (image.Title is not null)
                    builder.Append('|').Append(image.Title);
                builder.Append(']');
                return;
            case HtmlNode html:
                builder.Append("html\"").Append(html.Value).Append('"');
                return;
        }

        builder.Append(node switch
        {
            ParagraphNode => "p",
            HeadingNode heading => "h" + heading.Level,
            StrongNode => "strong",
            EmphasisNode => "em",
            StrikethroughNode => "del",
            BlockQuoteNode => "quote",
            LinkNode link => "a[" + link.Url + (link.Title is null ? string.Empty : "|" + link.Title) + "]",
            ListNode { Ordered: true } list => "ol" + list.Start,
            ListNode => "ul",
            ListItemNode { Checked: true } => "li[x]",
            ListItemNode { Checked: false } => "li[ ]",
            ListItemNode => "li",
            TableNode table => "table[" + string.Join(",", table.Alignment.Select(a => a.ToString()[0])) + "]",
            TableRowNode => "tr",
            TableCellNode => "td",
            _ => node.GetType().Name,
        });
        builder.Append('(');
        AppendNodes(builder, node.Children);
        builder.Append(')');
    }

    // Pushes in reverse, so the first item is handled first.
    private static void Push<T>(Stack<object> pending, IReadOnlyList<T> items)
        where T : notnull
    {
        for (var i = items.Count - 1; i >= 0; i--)
            pending.Push(items[i]);
    }
}
