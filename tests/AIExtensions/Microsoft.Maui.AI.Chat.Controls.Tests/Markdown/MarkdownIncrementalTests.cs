using System.Text;
using Microsoft.Maui.AI.Chat;
using static Microsoft.Maui.AI.Chat.Controls.Tests.Markdown.MarkdownTestHelpers;

namespace Microsoft.Maui.AI.Chat.Controls.Tests.Markdown;

/// <summary>Streaming updates change the views of what changed, in place, and end where a fresh render would.</summary>
public class MarkdownIncrementalTests
{
    // Quotes, lists, code, images and a table nested the ways answers nest them, with a footnote and a reference.
    internal const string NestedAnswer = """
        > Quote with **bold**
        > - item in quote
        >   1. nested ordered
        > ![quoted](https://example.com/q.png) after the image

        1. First
           > quoted in a list
           ```js
           code in a list
           ```
        2. Second ![pic](https://example.com/p.png)
           continued text
        - [ ] task
        - [x] done

        Here is the chart:
        ![chart](https://example.com/c.png)
        As you can see, it works.

        | A | B |
        |---|--:|
        | 1 | 2 |
        | 3 | **4** |

        Text with a footnote[^1] and a [reference][r].

        [^1]: A footnote with *emphasis*

        [r]: https://example.com/r
        """;

    public static TheoryData<string> Documents => new()
    {
        MarkdownStreamingTests.LlmAnswer,
        NestedAnswer,
        LongList(12),
    };

    [Theory]
    [MemberData(nameof(Documents))]
    public void StreamedViews_MatchAFreshRender_AtEveryLength(string document)
    {
        var streamed = new MarkdownView();
        for (var length = 1; length <= document.Length; length++)
        {
            var prefix = document[..length];
            streamed.SetMarkdown(prefix);

            var fresh = new MarkdownView();
            fresh.SetMarkdown(prefix);
            var expected = Describe(fresh);
            var actual = Describe(streamed);
            if (expected != actual)
                Assert.Fail($"The streamed views differ from a fresh render at length {length}:\n{prefix}\n\nexpected:\n{expected}\nactual:\n{actual}");
        }
    }

    [Fact]
    public void StreamingALongList_BuildsAboutAsManyElementsAsItShows()
    {
        // 60 items with two sub-items each, streamed 20 characters at a time.
        var list = LongList(60);
        var view = new MarkdownView();
        var added = 0;
        view.DescendantAdded += (_, _) => added++;
        for (var length = 20; length < list.Length + 20; length += 20)
            view.SetMarkdown(list[..Math.Min(length, list.Length)]);

        // Rebuilding the list on every update added about 57,000 elements to show these 483.
        var shown = Descendants(view).Count() - 1;
        Assert.InRange(shown, 480, 490);
        Assert.True(added < shown * 3, $"Added {added} elements to show {shown}.");

        var fresh = new MarkdownView();
        fresh.SetMarkdown(list);
        Assert.Equal(Describe(fresh), Describe(view));
    }

    [Fact]
    public void AGrowingParagraph_KeepsItsLabelAndItsImages()
    {
        var view = new MarkdownView();
        view.SetMarkdown("Here is the chart");
        var label = Assert.IsAssignableFrom<Label>(view.BlockViews[0]);

        view.SetMarkdown("Here is the chart, which grows");
        Assert.Same(label, view.BlockViews[0]);
        Assert.Equal("Here is the chart, which grows", label.Text);

        view.SetMarkdown("![chart](https://example.com/c.png)");
        var image = Assert.IsType<LinkedImageView>(view.BlockViews[0]);

        // The paragraph grows around its image; the image stays, so it does not load or resize again.
        view.SetMarkdown("![chart](https://example.com/c.png)\nAs you can see");
        var parts = Assert.IsAssignableFrom<VerticalStackLayout>(view.BlockViews[0]).Children;
        Assert.Same(image, parts[0]);
        var after = Assert.IsAssignableFrom<Label>(parts[1]);

        view.SetMarkdown("![chart](https://example.com/c.png)\nAs you can see, **it works**");
        parts = Assert.IsAssignableFrom<VerticalStackLayout>(view.BlockViews[0]).Children;
        Assert.Same(image, parts[0]);
        Assert.Same(after, parts[1]);
        Assert.Equal("it works", after.FormattedText.Spans[^1].Text);
    }

    [Fact]
    public void AGrowingCodeBlock_KeepsItsViews()
    {
        var view = new MarkdownView();
        view.SetMarkdown("```csharp\nvar a = 1;");
        var block = view.BlockViews[0];
        var code = Labels(block!).Single(label => label.Text.StartsWith("var", StringComparison.Ordinal));

        view.SetMarkdown("```csharp\nvar a = 1;\nvar b = 2;");

        Assert.Same(block, view.BlockViews[0]);
        Assert.Equal("var a = 1;\nvar b = 2;", code.Text);
    }

    [Fact]
    public void AGrowingList_KeepsItsItems()
    {
        var view = new MarkdownView();
        view.SetMarkdown("- one\n- two");
        var grid = Assert.IsAssignableFrom<Grid>(view.BlockViews[0]);
        var one = grid.Children.OfType<Label>().Single(label => label.Text == "one");
        var two = grid.Children.OfType<Label>().Single(label => label.Text == "two");

        view.SetMarkdown("- one\n- two grows\n  - nested\n- three");

        // The second item now stacks its text above a nested list; its label moved into that stack.
        Assert.Same(grid, view.BlockViews[0]);
        Assert.Contains(one, grid.Children);
        Assert.Equal("two grows", two.Text);
        var second = Assert.IsAssignableFrom<VerticalStackLayout>(grid.Children.Single(child => Grid.GetRow((BindableObject)child) == 1 && Grid.GetColumn((BindableObject)child) == 1));
        Assert.Same(two, second.Children[0]);
        Assert.Equal(3, grid.RowDefinitions.Count);
    }

    [Fact]
    public void AGrowingTable_KeepsItsRows()
    {
        var view = new MarkdownView();
        view.SetMarkdown("| A | B |\n|---|---|\n| 1 | 2 |\n| 3 |");
        var table = view.BlockViews[0]!;
        var cells = Labels(table).ToArray();

        view.SetMarkdown("| A | B |\n|---|---|\n| 1 | 2 |\n| 3 | 4 |\n| 5 | 6 |");

        Assert.Same(table, view.BlockViews[0]);
        var now = Labels(table).ToArray();
        Assert.Equal(cells, now.Take(cells.Length));
        Assert.Equal(["A", "B", "1", "2", "3", "4", "5", "6"], now.Select(label => label.Text));
    }

    [Fact]
    public void AViewThatMovesIntoAStack_GetsBackTheMarginTrimmedOffIt()
    {
        // The first block's heading has its top margin trimmed; once the block stacks it above a paragraph, the
        // heading is no longer the first view and keeps its margin, as in a fresh render.
        var view = new MarkdownView();
        view.SetDocument([With(new FootnoteNode(), With(new HeadingNode(1), new TextNode("Title")))]);
        var heading = Assert.IsAssignableFrom<Label>(view.BlockViews[0]);
        Assert.Equal(0, heading.Margin.Top);

        RichTextNode[] grown = [With(new FootnoteNode(), With(new HeadingNode(1), new TextNode("Title")), Paragraph("Text"))];
        view.SetDocument(grown);

        var stack = Assert.IsAssignableFrom<VerticalStackLayout>(view.BlockViews[0]);
        Assert.Same(heading, stack.Children[0]);
        var fresh = new MarkdownView();
        fresh.SetDocument(grown);
        Assert.Equal(Describe(fresh), Describe(view));
        Assert.True(heading.Margin.Top > 0);
    }

    [Fact]
    public void ATableTooBigForAViewPerCell_ShowsAsText()
    {
        static string Table(int columns) =>
            "|" + string.Concat(Enumerable.Repeat(" a |", columns)) + "\n"
            + "|" + string.Concat(Enumerable.Repeat("---|", columns)) + "\n"
            + "|" + string.Concat(Enumerable.Repeat(" b |", columns));

        var view = new MarkdownView();
        view.SetMarkdown(Table(MarkdownRenderer.MaxTableColumns));
        Assert.Equal(2 * MarkdownRenderer.MaxTableColumns, Labels(view).Count());

        // 12,000 columns took seconds to lay out as views.
        view.SetMarkdown(Table(12_000));
        var text = Assert.Single(Labels(view));
        Assert.StartsWith("a | a | a", text.Text, StringComparison.Ordinal);
        Assert.Contains("\nb | b | b", text.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void AParagraphWithThousandsOfMarkers_IsNotParsedForFormatting()
    {
        // Parsing emphasis and links takes time quadratic in their markers: 48 KB of "[a(" or "*a" took most of a
        // second, again on every streaming update. Past the limit a heading or paragraph keeps its text, unformatted.
        const string Unit = "*a* [b](https://e.com) ";
        var heavy = string.Concat(Enumerable.Repeat(Unit, (MarkdownRichTextParser.MaxInlineMarkers / 3) + 1));
        var nodes = MarkdownRichTextParser.ParseWithoutFallback($"# {heavy}\n\n{heavy}\n\n- {heavy}\n\n> {heavy}\n\nThe **end**");

        Assert.Equal(5, nodes.Count);
        Assert.IsType<HeadingNode>(nodes[0]);
        foreach (var node in nodes.Take(4))
        {
            Assert.Equal(heavy.Trim(), GetVisibleText([node]).Trim());
            Assert.DoesNotContain(Flatten(node), child => child is EmphasisNode or LinkNode);
        }

        Assert.IsType<StrongNode>(nodes[4].Children[1]);

        // Below the limit, the same text formats as usual.
        var light = string.Concat(Enumerable.Repeat(Unit, (MarkdownRichTextParser.MaxInlineMarkers / 3) - 1));
        var formatted = Flatten(MarkdownRichTextParser.ParseWithoutFallback(light)[0]).ToList();
        Assert.Contains(formatted, child => child is EmphasisNode);
        Assert.Contains(formatted, child => child is LinkNode);
    }

    [Fact]
    public void PathologicalMarkdown_KeepsAllItsText()
    {
        foreach (var unit in new[] { "[a(", "*a ", "_a ", "[a](", "![a(", "~~a " })
        {
            var text = string.Concat(Enumerable.Repeat(unit, 48 * 1024 / unit.Length));
            Assert.Equal(text.Trim(), GetVisibleText(MarkdownRichTextParser.Parse(text)).Trim());
        }
    }

    internal static string LongList(int items)
    {
        var builder = new StringBuilder();
        for (var i = 1; i <= items; i++)
        {
            builder.Append("- Item number ").Append(i).Append(" has some text\n");
            builder.Append("  - first sub point ").Append(i).Append('\n');
            builder.Append("  - second sub point ").Append(i).Append('\n');
        }

        return builder.ToString();
    }

    private static IEnumerable<RichTextNode> Flatten(RichTextNode node) =>
        node.Children.SelectMany(child => Flatten(child).Prepend(child));

    // What shows: element types, text and spans, sizes, alignment, margins, grid tracks and positions, and image
    // sources. Grid children are listed by position: an updated grid may hold them in another order.
    internal static string Describe(Element root)
    {
        var builder = new StringBuilder();
        Append(root, 0);
        return builder.ToString();

        void Append(Element element, int depth)
        {
            builder.Append(' ', depth * 2).Append(element.GetType().Name);
            if (element is View view)
            {
                builder.Append(" margin=").Append(view.Margin).Append(" h=").Append(view.HorizontalOptions.Alignment);
                if (element.Parent is Grid)
                    builder.Append($" cell={Grid.GetRow(view)},{Grid.GetColumn(view)},{Grid.GetRowSpan(view)},{Grid.GetColumnSpan(view)}");
                var heading = SemanticProperties.GetHeadingLevel(view);
                if (heading != SemanticHeadingLevel.None)
                    builder.Append(" heading=").Append(heading);
                if (SemanticProperties.GetDescription(view) is { } description)
                    builder.Append(" description=").Append(description);
            }

            switch (element)
            {
                case Label label:
                    builder.Append($" font={label.FontSize} {label.FontAttributes} {label.HorizontalTextAlignment} {label.LineBreakMode} pad={label.Padding} maxw={label.MaximumWidthRequest}");
                    if (label.FormattedText is { } formatted)
                    {
                        foreach (var span in formatted.Spans)
                        {
                            var link = span.GestureRecognizers.OfType<TapGestureRecognizer>().Select(tap => tap.CommandParameter).SingleOrDefault();
                            builder.Append($" [{span.Text}|{span.FontAttributes}|{span.TextDecorations}|{span.FontSize}|{link}]");
                        }
                    }
                    else
                    {
                        builder.Append(" text=").Append(label.Text);
                    }

                    break;
                case Grid grid:
                    builder.Append(" rows=").AppendJoin(',', grid.RowDefinitions.Select(row => row.Height))
                        .Append(" columns=").AppendJoin(',', grid.ColumnDefinitions.Select(column => column.Width))
                        .Append($" spacing={grid.RowSpacing},{grid.ColumnSpacing}");
                    break;
                case StackBase stack:
                    builder.Append(" spacing=").Append(stack.Spacing);
                    break;
                case Image image:
                    builder.Append(" source=").Append((image.Source as UriImageSource)?.Uri).Append(" max=").Append(image.MaximumWidthRequest).Append('x').Append(image.MaximumHeightRequest);
                    break;
                case ScrollView scroll:
                    builder.Append(' ').Append(scroll.Orientation).Append(' ').Append(scroll.HorizontalScrollBarVisibility);
                    break;
                case BoxView box:
                    builder.Append($" size={box.WidthRequest}x{box.HeightRequest}");
                    break;
            }

            builder.Append('\n');
            IEnumerable<Element> children = ((IVisualTreeElement)element).GetVisualChildren().OfType<Element>();
            if (element is Grid)
            {
                children = children
                    .OrderBy(child => Grid.GetRow(child))
                    .ThenBy(child => Grid.GetColumn(child))
                    .ToList();
            }

            foreach (var child in children)
                Append(child, depth + 1);
        }
    }

    private static IEnumerable<Element> Descendants(Element root)
    {
        var pending = new Stack<Element>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var element = pending.Pop();
            yield return element;
            foreach (var child in ((IVisualTreeElement)element).GetVisualChildren().OfType<Element>().Reverse())
                pending.Push(child);
        }
    }

    private static IEnumerable<Label> Labels(Element root) => Descendants(root).OfType<Label>();
}
