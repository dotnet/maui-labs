// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Markdig;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>
/// Converts the Markdown that language models produce into the engine's rich-text node model.
/// </summary>
/// <remarks>
/// Markdig does the parsing (CommonMark plus GitHub-style tables, task lists, autolinks and
/// strikethrough); this class only maps its syntax tree. Raw HTML is disabled so it shows as
/// text, and every newline inside a paragraph becomes a <see cref="LineBreakNode"/>, as chat
/// answers expect. Unfinished input that is still streaming in maps like any other document, so
/// an unclosed code fence is a code block and an unclosed <c>**</c> is literal text.
/// </remarks>
internal static class MarkdownRichTextParser
{
    private static readonly MarkdownPipeline s_pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseTaskLists()
        .UseAutoLinks()
        .UseEmphasisExtras(EmphasisExtraOptions.Strikethrough)
        .UseSoftlineBreakAsHardlineBreak()
        .DisableHtml()
        .Build();

    /// <summary>Parses <paramref name="markdown"/> into top-level block nodes. Never throws.</summary>
    internal static IReadOnlyList<RichTextNode> Parse(string markdown)
    {
        if (string.IsNullOrEmpty(markdown))
            return Array.Empty<RichTextNode>();

        try
        {
            return ParseWithoutFallback(markdown);
        }
        catch (Exception)
        {
            // Markdig rejects input nested deeper than 128 levels, and a mapping defect must
            // never take down the chat UI either, so show the raw text, which keeps every character.
            return [CreateLiteral(markdown)];
        }
    }

    /// <summary>Parses without the defensive fallback so tests can observe failures.</summary>
    internal static IReadOnlyList<RichTextNode> ParseWithoutFallback(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        if (markdown.Contains('\0', StringComparison.Ordinal))
            markdown = markdown.Replace('\0', '\uFFFD');

        var document = new DocumentNode();
        AddBlocks(document, Markdown.Parse(markdown, s_pipeline), markdown);
        return document.Children;
    }

    // ── Blocks ──────────────────────────────────────────────────────────

    private static void AddBlocks(RichTextNode parent, ContainerBlock container, string source)
    {
        foreach (var block in container)
        {
            if (MapBlock(block, source) is { } node)
                parent.AddChild(node);
        }
    }

    private static RichTextNode? MapBlock(Block block, string source)
    {
        // Markdig's nesting limit bounds the recursion; this is the backstop.
        RuntimeHelpers.EnsureSufficientExecutionStack();

        switch (block)
        {
            case LinkReferenceDefinitionGroup or BlankLineBlock:
                return null;
            case HeadingBlock heading:
                return MapLeaf(new HeadingNode(Math.Clamp(heading.Level, 1, 6)), heading, source);
            case ParagraphBlock paragraph:
                return MapLeaf(new ParagraphNode(), paragraph, source);
            case CodeBlock code:
                // Markdig keeps the first word of the info string in Info. An unclosed fence
                // holds every line streamed so far.
                var language = code is FencedCodeBlock { Info: { Length: > 0 } info } ? info : null;
                return new CodeBlockNode(code.Lines.ToString(), language);
            case QuoteBlock quote:
                var quoteNode = new BlockQuoteNode();
                AddBlocks(quoteNode, quote, source);
                return quoteNode;
            case ListBlock list:
                return MapList(list, source);
            case ThematicBreakBlock:
                return new ThematicBreakNode();
            case Table table:
                return MapTable(table, source);
            default:
                // HTML blocks (disabled in the pipeline) and future block types show their source.
                return CreateLiteral(GetSource(block, source));
        }
    }

    // An inline without a mapping shows the block's source instead, so no text is lost.
    private static RichTextNode MapLeaf(RichTextNode node, LeafBlock leaf, string source) =>
        AddInlines(node, leaf.Inline) ? node : CreateLiteral(GetSource(leaf, source));

    private static ListNode MapList(ListBlock list, string source)
    {
        int? start = null;
        if (list.IsOrdered)
            start = int.TryParse(list.OrderedStart, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : 1;

        var node = new ListNode(list.IsOrdered, start);
        foreach (var item in list.OfType<ListItemBlock>())
        {
            var itemNode = new ListItemNode();

            // A task marker is a TaskList inline that opens the item's first paragraph.
            if (item.Count > 0 && item[0] is ParagraphBlock { Inline.FirstChild: TaskList task })
            {
                itemNode.Checked = task.Checked;
                if (task.NextSibling is LiteralInline text)
                    text.Content.TrimStart();
                task.Remove();
            }

            AddBlocks(itemNode, item, source);
            node.AddChild(itemNode);
        }

        return node;
    }

    private static RichTextNode MapTable(Table table, string source)
    {
        var node = new TableNode
        {
            Alignment = table.ColumnDefinitions?.Select(static column => column.Alignment switch
            {
                TableColumnAlign.Left => TableColumnAlignment.Left,
                TableColumnAlign.Center => TableColumnAlignment.Center,
                TableColumnAlign.Right => TableColumnAlignment.Right,
                _ => TableColumnAlignment.None,
            }).ToArray() ?? [],
        };

        // Rows keep source order, so the header row comes first as the node model expects.
        foreach (var row in table.OfType<TableRow>())
        {
            var rowNode = new TableRowNode();
            foreach (var cell in row.OfType<TableCell>())
            {
                // A cell holds one paragraph, whose inlines map straight into the cell node.
                var cellNode = new TableCellNode();
                foreach (var block in cell)
                {
                    if (block is not LeafBlock leaf || !AddInlines(cellNode, leaf.Inline))
                        return CreateLiteral(GetSource(table, source));
                }

                rowNode.AddChild(cellNode);
            }

            node.AddChild(rowNode);
        }

        return node;
    }

    // ── Inlines ─────────────────────────────────────────────────────────

    // Returns false when an inline has no mapping, so the caller can show the block's source.
    private static bool AddInlines(RichTextNode parent, ContainerInline? container)
    {
        var text = new StringBuilder();
        var mapped = AppendInlines(parent, container, text);
        FlushText(parent, text);
        return mapped;
    }

    // Consecutive text collects in `text` and becomes one TextNode before the next element.
    private static bool AppendInlines(RichTextNode parent, ContainerInline? container, StringBuilder text)
    {
        RuntimeHelpers.EnsureSufficientExecutionStack();

        for (var inline = container?.FirstChild; inline is not null; inline = inline.NextSibling)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    text.Append(literal.Content.AsSpan());
                    break;
                case CodeInline code:
                    AddElement(new InlineCodeNode(code.Content));
                    break;
                case LineBreakInline { NextSibling: null, Parent.Parent: null }:
                    // A break that ends its block, e.g. before a table that interrupts a
                    // paragraph, shows nothing in CommonMark.
                    break;
                case LineBreakInline lineBreak:
                    if (lineBreak.IsHard)
                        AddElement(new LineBreakNode());
                    else
                        text.Append(' ');
                    break;
                case HtmlEntityInline entity:
                    text.Append(entity.Transcoded.AsSpan());
                    break;
                case HtmlInline html:
                    text.Append(html.Tag);
                    break;
                case TaskList task:
                    text.Append(task.Checked ? "[x]" : "[ ]");
                    break;
                case AutolinkInline autolink:
                    AddElement(new LinkNode(autolink.IsEmail ? "mailto:" + autolink.Url : autolink.Url))
                        .AddChild(new TextNode(autolink.Url));
                    break;
                case LinkInline { IsImage: true } image:
                    var alt = new ParagraphNode();
                    AddInlines(alt, image);
                    AddElement(new ImageNode(image.Url ?? string.Empty, GetText(alt), NullIfEmpty(image.Title)));
                    break;
                case LinkInline link:
                    if (!AddInlines(AddElement(new LinkNode(link.Url ?? string.Empty, NullIfEmpty(link.Title))), link))
                        return false;
                    break;
                case EmphasisInline emphasis:
                    RichTextNode styled = emphasis.DelimiterChar == '~' ? new StrikethroughNode()
                        : emphasis.DelimiterCount >= 2 ? new StrongNode()
                        : new EmphasisNode();
                    if (!AddInlines(AddElement(styled), emphasis))
                        return false;
                    break;
                case DelimiterInline delimiter:
                    // An unmatched delimiter is literal text.
                    text.Append(delimiter.ToLiteral());
                    if (!AppendInlines(parent, delimiter, text))
                        return false;
                    break;
                case ContainerInline other:
                    if (!AppendInlines(parent, other, text))
                        return false;
                    break;
                default:
                    return false;
            }
        }

        return true;

        T AddElement<T>(T node)
            where T : RichTextNode
        {
            FlushText(parent, text);
            parent.AddChild(node);
            return node;
        }
    }

    private static void FlushText(RichTextNode parent, StringBuilder text)
    {
        if (text.Length == 0)
            return;

        parent.AddChild(new TextNode(text.ToString()));
        text.Clear();
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private static string GetText(RichTextNode node) => node switch
    {
        TextNode text => text.Text,
        InlineCodeNode code => code.Code,
        ImageNode image => image.Alt ?? string.Empty,
        LineBreakNode => " ",
        _ => string.Concat(node.Children.Select(GetText)),
    };

    private static string GetSource(Block block, string source)
    {
        var span = block.Span;
        return span.Start >= 0 && span.Length > 0 && span.End < source.Length
            ? source.Substring(span.Start, span.Length)
            : string.Empty;
    }

    // A paragraph that shows text verbatim, with a LineBreakNode for every newline.
    private static ParagraphNode CreateLiteral(string text)
    {
        var paragraph = new ParagraphNode();
        var lines = text.ReplaceLineEndings("\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
                paragraph.AddChild(new LineBreakNode());
            if (lines[i].Length > 0)
                paragraph.AddChild(new TextNode(lines[i]));
        }

        return paragraph;
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    private sealed class DocumentNode : RichTextNode;
}
