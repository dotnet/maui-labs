using System.Text;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>Renders the structured node tree of a <see cref="RichContentBlock"/>.</summary>
internal sealed class RichTextBlockView : BlockView
{
    private Dictionary<string, DefinitionNode> _definitions = new(StringComparer.OrdinalIgnoreCase);

    public RichTextBlockView() => AutomationId = "AIChatRichText";

    internal View? RenderedContent => Content;

    internal static bool IsSafeUri(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "mailto";

    protected override void OnBlockChanged()
    {
        if (Block is not RichContentBlock rich)
        {
            Content = null;
            return;
        }

        _definitions = new(StringComparer.OrdinalIgnoreCase);
        CollectDefinitions(rich.Content);
        Content = RenderDocument(rich);
    }

    private View RenderDocument(RichContentBlock rich)
    {
        if (rich.Content.Count == 0)
            return CreateTextLabel(rich.RawText);

        var layout = new VerticalStackLayout { Spacing = 8 };
        foreach (var node in rich.Content)
        {
            if (node is not DefinitionNode)
                layout.Children.Add(RenderBlock(node));
        }

        return layout;
    }

    private View RenderBlock(RichTextNode node) =>
        node switch
        {
            HeadingNode heading => RenderHeading(heading),
            ParagraphNode paragraph => CreateInlineLabel(paragraph.Children),
            CodeBlockNode code => RenderCodeBlock(code.Code),
            BlockQuoteNode quote => RenderBlockQuote(quote),
            ListNode list => RenderList(list),
            TableNode table => RenderTable(table),
            ThematicBreakNode => CreateRule(),
            ImageNode image => RenderImage(image.Url, image.Alt),
            ImageReferenceNode reference => RenderImageReference(reference),
            HtmlNode html => RenderCodeBlock(html.Value),
            FootnoteDefinitionNode footnote => RenderFootnoteDefinition(footnote),
            FootnoteNode footnote => RenderContainer(footnote.Children),
            _ => CreateInlineLabel([node]),
        };

    private Label RenderHeading(HeadingNode heading)
    {
        var label = CreateInlineLabel(heading.Children);
        label.FontAttributes = FontAttributes.Bold;
        label.FontSize = Math.Clamp(26 - (heading.Level * 2), 15, 24);
        SemanticProperties.SetHeadingLevel(label, (SemanticHeadingLevel)Math.Clamp(heading.Level, 1, 9));
        return label;
    }

    private static View RenderCodeBlock(string code) =>
        ChatVisuals.CodeBlock(ChatVisuals.Label(AIChatThemeKeys.CodeTextStyle, code));

    private View RenderBlockQuote(BlockQuoteNode quote)
    {
        var bar = new BoxView { WidthRequest = 3, VerticalOptions = LayoutOptions.Fill };
        bar.SetDynamicResource(BoxView.ColorProperty, AIChatThemeKeys.StrokeColor);
        var content = RenderContainer(quote.Children);
        var grid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) },
            ColumnSpacing = 10,
        };
        grid.Add(bar, 0);
        grid.Add(content, 1);
        return grid;
    }

    private View RenderList(ListNode list)
    {
        var layout = new VerticalStackLayout { Spacing = 4 };
        var number = list.Start ?? 1;
        foreach (var child in list.Children)
        {
            if (child is not ListItemNode item)
                continue;

            var marker = item.Checked switch
            {
                true => "\u2611",
                false => "\u2610",
                null when list.Ordered => $"{number++}.",
                _ => "\u2022",
            };
            var row = new Grid
            {
                ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) },
                ColumnSpacing = 8,
            };
            row.Add(CreateTextLabel(marker), 0);
            row.Add(RenderContainer(item.Children), 1);
            layout.Children.Add(row);
        }

        return layout;
    }

    private View RenderTable(TableNode table)
    {
        var rows = table.Children.OfType<TableRowNode>().ToArray();
        if (rows.Length == 0)
            return new ContentView();

        var columnCount = rows.Max(static row => row.Children.Count);
        var grid = new Grid { RowSpacing = 1, ColumnSpacing = 1 };
        grid.SetDynamicResource(BackgroundColorProperty, AIChatThemeKeys.StrokeColor);
        for (var column = 0; column < columnCount; column++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var cells = rows[rowIndex].Children.OfType<TableCellNode>().ToArray();
            for (var column = 0; column < columnCount; column++)
            {
                var label = column < cells.Length ? CreateInlineLabel(cells[column].Children) : CreateTextLabel(string.Empty);
                label.Padding = new Thickness(8, 6);
                label.HorizontalTextAlignment = GetAlignment(table, column);
                label.SetDynamicResource(BackgroundColorProperty, AIChatThemeKeys.SurfaceColor);
                if (rowIndex == 0)
                    label.FontAttributes = FontAttributes.Bold;
                grid.Add(label, column, rowIndex);
            }
        }

        return new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = grid };
    }

    private static TextAlignment GetAlignment(TableNode table, int column) =>
        column < table.Alignment.Count
            ? table.Alignment[column] switch
            {
                TableColumnAlignment.Center => TextAlignment.Center,
                TableColumnAlignment.Right => TextAlignment.End,
                _ => TextAlignment.Start,
            }
            : TextAlignment.Start;

    private View RenderImageReference(ImageReferenceNode reference) =>
        _definitions.TryGetValue(reference.Label, out var definition)
            ? RenderImage(definition.Url, reference.Alt)
            : CreateTextLabel(reference.Alt ?? $"[{reference.Label}]");

    private View RenderImage(string url, string? alt)
    {
        if (!IsSafeUri(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return CreateTextLabel(alt ?? url);

        var image = new Image
        {
            Source = ImageSource.FromUri(uri),
            Aspect = Aspect.AspectFit,
            MaximumHeightRequest = 320,
            HorizontalOptions = LayoutOptions.Start,
        };
        SemanticProperties.SetDescription(image, alt ?? "Image");
        return image;
    }

    private View RenderFootnoteDefinition(FootnoteDefinitionNode footnote)
    {
        var row = new HorizontalStackLayout { Spacing = 4 };
        row.Children.Add(CreateTextLabel($"[{footnote.Label}]"));
        row.Children.Add(RenderContainer(footnote.Children));
        return row;
    }

    private View RenderContainer(IReadOnlyList<RichTextNode> children)
    {
        if (children.Count == 0)
            return new ContentView();

        if (children.All(IsInlineNode))
            return CreateInlineLabel(children);

        var layout = new VerticalStackLayout { Spacing = 6 };
        foreach (var child in children)
            layout.Children.Add(RenderBlock(child));
        return layout;
    }

    private static BoxView CreateRule()
    {
        var rule = new BoxView { HeightRequest = 1, HorizontalOptions = LayoutOptions.Fill };
        rule.SetDynamicResource(BoxView.ColorProperty, AIChatThemeKeys.StrokeColor);
        return rule;
    }

    private Label CreateInlineLabel(IReadOnlyList<RichTextNode> nodes)
    {
        var formatted = new FormattedString();
        foreach (var node in nodes)
            AppendInline(formatted, node, FontAttributes.None, TextDecorations.None);

        return new Label { FormattedText = formatted }.Styled(AIChatThemeKeys.AssistantTextStyle);
    }

    private static Label CreateTextLabel(string text) => ChatVisuals.Label(AIChatThemeKeys.AssistantTextStyle, text);

    private void AppendInline(FormattedString formatted, RichTextNode node, FontAttributes attributes, TextDecorations decorations)
    {
        switch (node)
        {
            case TextNode text:
                formatted.Spans.Add(CreateSpan(text.Text, attributes, decorations));
                return;
            case InlineCodeNode code:
                var codeSpan = CreateSpan(code.Code, attributes, decorations);
                codeSpan.SetDynamicResource(Span.FontFamilyProperty, AIChatThemeKeys.CodeFontFamily);
                codeSpan.SetDynamicResource(Span.BackgroundColorProperty, AIChatThemeKeys.CodeBackgroundColor);
                formatted.Spans.Add(codeSpan);
                return;
            case LineBreakNode:
                formatted.Spans.Add(CreateSpan("\n", attributes, decorations));
                return;
            case StrongNode strong:
                AppendChildren(formatted, strong.Children, attributes | FontAttributes.Bold, decorations);
                return;
            case EmphasisNode emphasis:
                AppendChildren(formatted, emphasis.Children, attributes | FontAttributes.Italic, decorations);
                return;
            case StrikethroughNode strike:
                AppendChildren(formatted, strike.Children, attributes, decorations | TextDecorations.Strikethrough);
                return;
            case LinkNode link:
                AppendLink(formatted, link.Url, link.Children, attributes, decorations);
                return;
            case LinkReferenceNode reference when _definitions.TryGetValue(reference.Label, out var definition):
                AppendLink(formatted, definition.Url, reference.Children, attributes, decorations);
                return;
            case FootnoteReferenceNode footnote:
                formatted.Spans.Add(CreateSpan($"[{footnote.Label}]", attributes, decorations));
                return;
            default:
                AppendChildren(formatted, node.Children, attributes, decorations);
                return;
        }
    }

    private void AppendLink(FormattedString formatted, string url, IReadOnlyList<RichTextNode> children, FontAttributes attributes, TextDecorations decorations)
    {
        var text = GetPlainText(children);
        var span = CreateSpan(text.Length == 0 ? url : text, attributes, decorations);
        if (IsSafeUri(url) && Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            span.SetDynamicResource(Span.StyleProperty, AIChatThemeKeys.LinkSpanStyle);
            span.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(async () => await OpenAsync(uri)) });
        }

        formatted.Spans.Add(span);
    }

    private static async Task OpenAsync(Uri uri)
    {
        try
        {
            await Launcher.Default.OpenAsync(uri);
        }
        catch (Exception ex) when (ex is FeatureNotSupportedException or InvalidOperationException or NotImplementedException)
        {
            // No browser or launcher on this platform; the link text stays readable.
        }
    }

    private void AppendChildren(FormattedString formatted, IReadOnlyList<RichTextNode> children, FontAttributes attributes, TextDecorations decorations)
    {
        foreach (var child in children)
            AppendInline(formatted, child, attributes, decorations);
    }

    private static Span CreateSpan(string text, FontAttributes attributes, TextDecorations decorations) =>
        new() { Text = text, FontAttributes = attributes, TextDecorations = decorations };

    private static bool IsInlineNode(RichTextNode node) =>
        node is TextNode or InlineCodeNode or LineBreakNode or StrongNode or EmphasisNode
            or StrikethroughNode or LinkNode or LinkReferenceNode or FootnoteReferenceNode;

    private static string GetPlainText(IReadOnlyList<RichTextNode> nodes)
    {
        var builder = new StringBuilder();
        AppendPlainText(builder, nodes);
        return builder.ToString();
    }

    private static void AppendPlainText(StringBuilder builder, IReadOnlyList<RichTextNode> nodes)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case TextNode text:
                    builder.Append(text.Text);
                    break;
                case InlineCodeNode code:
                    builder.Append(code.Code);
                    break;
                case LineBreakNode:
                    builder.AppendLine();
                    break;
                default:
                    AppendPlainText(builder, node.Children);
                    break;
            }
        }
    }

    private void CollectDefinitions(IReadOnlyList<RichTextNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node is DefinitionNode { Label.Length: > 0 } definition)
                _definitions[definition.Label] = definition;
            CollectDefinitions(node.Children);
        }
    }
}
