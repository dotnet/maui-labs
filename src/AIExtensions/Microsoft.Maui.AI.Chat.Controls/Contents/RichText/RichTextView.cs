using Microsoft.Maui.AI.Chat;
using Microsoft.Maui.AI.Chat.Controls.Themes;
using Microsoft.Maui.Controls.Shapes;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>Renders the structured node tree of a <see cref="RichContentBlock"/> inside a chat bubble.</summary>
public class RichTextView : ChatMessageView
{
    private Border? _messageBorder;
    private View? _renderedContent;
    private Dictionary<string, DefinitionNode> _definitions =
        new(StringComparer.OrdinalIgnoreCase);

    internal View? RenderedContent => _renderedContent;

    protected override void RefreshFromContentContext()
    {
        base.RefreshFromContentContext();

        if (ContentContext?.Block is not RichContentBlock rich)
            return;

        _definitions = [];
        CollectDefinitions(rich.Content);
        _renderedContent = RenderDocument(rich);
        ApplyRenderedContent();
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _messageBorder = GetTemplateChild("MessageBorder") as Border;
        ApplyRenderedContent();
    }

    internal static bool IsSafeUri(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https" or "mailto";
    }

    private void ApplyRenderedContent()
    {
        if (_messageBorder is not null && _renderedContent is not null)
            _messageBorder.Content = _renderedContent;
    }

    private View RenderDocument(RichContentBlock rich)
    {
        if (rich.Content.Count == 0)
            return CreateTextLabel(rich.RawText);

        var layout = new VerticalStackLayout
        {
            Spacing = 8,
        };

        foreach (var node in rich.Content)
        {
            if (node is DefinitionNode)
                continue;

            layout.Children.Add(RenderBlock(node));
        }

        return layout;
    }

    private View RenderBlock(RichTextNode node)
    {
        return node switch
        {
            HeadingNode heading => RenderHeading(heading),
            ParagraphNode paragraph => CreateInlineLabel(paragraph.Children),
            CodeBlockNode code => RenderCodeBlock(code),
            BlockQuoteNode quote => RenderBlockQuote(quote),
            ListNode list => RenderList(list),
            TableNode table => RenderTable(table),
            ThematicBreakNode => new BoxView
            {
                HeightRequest = 1,
                Opacity = 0.35,
                HorizontalOptions = LayoutOptions.Fill,
            },
            ImageNode image => RenderImage(image.Url, image.Alt),
            ImageReferenceNode imageReference => RenderImageReference(imageReference),
            HtmlNode html => RenderCodeLikeText(html.Value),
            FootnoteDefinitionNode footnote => RenderFootnoteDefinition(footnote),
            FootnoteNode footnote => RenderContainer(footnote.Children),
            _ => CreateInlineLabel([node]),
        };
    }

    private View RenderHeading(HeadingNode heading)
    {
        var label = CreateInlineLabel(heading.Children);
        label.FontAttributes = FontAttributes.Bold;
        label.FontSize = Math.Clamp(30 - (heading.Level * 2), 14, 28);
        return label;
    }

    private View RenderCodeBlock(CodeBlockNode code)
    {
        var label = CreateTextLabel(code.Code);
        label.FontFamily = "monospace";
        label.FontSize = 12;
        label.LineBreakMode = LineBreakMode.WordWrap;

        return new Border
        {
            Padding = new Thickness(10, 8),
            StrokeThickness = 0,
            BackgroundColor = Color.FromArgb("#18000000"),
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Content = label,
        };
    }

    private View RenderBlockQuote(BlockQuoteNode quote)
    {
        var content = RenderContainer(quote.Children);
        return new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(3)),
                new ColumnDefinition(GridLength.Star),
            },
            ColumnSpacing = 8,
            Children =
            {
                new BoxView
                {
                    Opacity = 0.5,
                    VerticalOptions = LayoutOptions.Fill,
                },
                content,
            },
        }.Apply(grid => Grid.SetColumn(content, 1));
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
                true => "☑",
                false => "☐",
                null when list.Ordered => $"{number++}.",
                _ => "•",
            };
            var content = RenderContainer(item.Children);
            var row = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Star),
                },
                ColumnSpacing = 8,
                Children =
                {
                    CreateTextLabel(marker),
                    content,
                },
            };
            Grid.SetColumn(content, 1);
            layout.Children.Add(row);
        }

        return layout;
    }

    private View RenderTable(TableNode table)
    {
        var rows = table.Children.OfType<TableRowNode>().ToArray();
        if (rows.Length == 0)
            return new ContentView();

        var columnCount = rows.Max(row => row.Children.Count);
        var grid = new Grid
        {
            RowSpacing = 1,
            ColumnSpacing = 1,
            BackgroundColor = Color.FromArgb("#30000000"),
        };
        for (var column = 0; column < columnCount; column++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var cells = rows[rowIndex].Children.OfType<TableCellNode>().ToArray();
            for (var column = 0; column < cells.Length; column++)
            {
                var label = CreateInlineLabel(cells[column].Children);
                label.Padding = new Thickness(8, 6);
                label.BackgroundColor = Color.FromArgb("#F8FFFFFF");
                label.HorizontalTextAlignment = GetAlignment(table, column);
                grid.Add(label, column, rowIndex);
            }
        }

        return new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            Content = grid,
        };
    }

    private static TextAlignment GetAlignment(TableNode table, int column)
    {
        if (column >= table.Alignment.Count)
            return TextAlignment.Start;

        return table.Alignment[column] switch
        {
            TableColumnAlignment.Center => TextAlignment.Center,
            TableColumnAlignment.Right => TextAlignment.End,
            _ => TextAlignment.Start,
        };
    }

    private View RenderImageReference(ImageReferenceNode reference)
    {
        return _definitions.TryGetValue(reference.Label, out var definition)
            ? RenderImage(definition.Url, reference.Alt)
            : CreateTextLabel(reference.Alt ?? $"[{reference.Label}]");
    }

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
        SemanticProperties.SetDescription(image, alt ?? "Rich text image");
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

    private Label CreateInlineLabel(IReadOnlyList<RichTextNode> nodes)
    {
        var formatted = new FormattedString();
        foreach (var node in nodes)
            AppendInline(formatted, node, FontAttributes.None, TextDecorations.None);

        var label = new Label
        {
            FormattedText = formatted,
            FontSize = 14,
            LineBreakMode = LineBreakMode.WordWrap,
        };
        ApplyTextColor(label);
        return label;
    }

    private void AppendInline(
        FormattedString formatted,
        RichTextNode node,
        FontAttributes attributes,
        TextDecorations decorations)
    {
        switch (node)
        {
            case TextNode text:
                formatted.Spans.Add(CreateSpan(text.Text, attributes, decorations));
                return;
            case InlineCodeNode code:
                formatted.Spans.Add(new Span
                {
                    Text = code.Code,
                    FontFamily = "monospace",
                    FontAttributes = attributes,
                    TextDecorations = decorations,
                });
                return;
            case LineBreakNode:
                formatted.Spans.Add(CreateSpan("\n", attributes, decorations));
                return;
            case StrongNode strong:
                AppendChildren(
                    formatted,
                    strong.Children,
                    attributes | FontAttributes.Bold,
                    decorations);
                return;
            case EmphasisNode emphasis:
                AppendChildren(
                    formatted,
                    emphasis.Children,
                    attributes | FontAttributes.Italic,
                    decorations);
                return;
            case StrikethroughNode strike:
                AppendChildren(
                    formatted,
                    strike.Children,
                    attributes,
                    decorations | TextDecorations.Strikethrough);
                return;
            case LinkNode link:
                AppendLink(formatted, link.Url, link.Children, attributes, decorations);
                return;
            case LinkReferenceNode reference:
                if (_definitions.TryGetValue(reference.Label, out var definition))
                {
                    AppendLink(
                        formatted,
                        definition.Url,
                        reference.Children,
                        attributes,
                        decorations);
                }
                else
                {
                    AppendChildren(formatted, reference.Children, attributes, decorations);
                }
                return;
            case FootnoteReferenceNode footnote:
                formatted.Spans.Add(CreateSpan(
                    $"[{footnote.Label}]",
                    attributes,
                    decorations));
                return;
            default:
                AppendChildren(formatted, node.Children, attributes, decorations);
                return;
        }
    }

    private void AppendLink(
        FormattedString formatted,
        string url,
        IReadOnlyList<RichTextNode> children,
        FontAttributes attributes,
        TextDecorations decorations)
    {
        var text = GetPlainText(children);
        var span = CreateSpan(
            text.Length == 0 ? url : text,
            attributes,
            decorations | TextDecorations.Underline);
        if (IsSafeUri(url) && Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            span.TextColor = Colors.Blue;
            span.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(async () => await Launcher.Default.OpenAsync(uri)),
            });
        }
        formatted.Spans.Add(span);
    }

    private void AppendChildren(
        FormattedString formatted,
        IReadOnlyList<RichTextNode> children,
        FontAttributes attributes,
        TextDecorations decorations)
    {
        foreach (var child in children)
            AppendInline(formatted, child, attributes, decorations);
    }

    private Span CreateSpan(
        string text,
        FontAttributes attributes,
        TextDecorations decorations)
    {
        var span = new Span
        {
            Text = text,
            FontAttributes = attributes,
            TextDecorations = decorations,
        };
        span.SetDynamicResource(
            Span.TextColorProperty,
            ContentContext?.IsUser == true
                ? ChatThemeKeys.UserTextColor
                : ChatThemeKeys.AssistantTextColor);
        return span;
    }

    private Label CreateTextLabel(string text)
    {
        var label = new Label
        {
            Text = text,
            FontSize = 14,
            LineBreakMode = LineBreakMode.WordWrap,
        };
        ApplyTextColor(label);
        return label;
    }

    private View RenderCodeLikeText(string text)
    {
        var label = CreateTextLabel(text);
        label.FontFamily = "monospace";
        label.FontSize = 12;
        return label;
    }

    private void ApplyTextColor(Label label)
    {
        label.SetDynamicResource(
            Label.TextColorProperty,
            ContentContext?.IsUser == true
                ? ChatThemeKeys.UserTextColor
                : ChatThemeKeys.AssistantTextColor);
    }

    private static bool IsInlineNode(RichTextNode node)
    {
        return node is TextNode
            or InlineCodeNode
            or LineBreakNode
            or StrongNode
            or EmphasisNode
            or StrikethroughNode
            or LinkNode
            or LinkReferenceNode
            or FootnoteReferenceNode;
    }

    private static string GetPlainText(IReadOnlyList<RichTextNode> nodes)
    {
        var builder = new System.Text.StringBuilder();
        AppendPlainText(builder, nodes);
        return builder.ToString();
    }

    private static void AppendPlainText(
        System.Text.StringBuilder builder,
        IReadOnlyList<RichTextNode> nodes)
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
            if (node is DefinitionNode definition
                && definition.Label.Length > 0)
            {
                _definitions[definition.Label] = definition;
            }
            CollectDefinitions(node.Children);
        }
    }
}

internal static class ViewExtensions
{
    internal static T Apply<T>(this T value, Action<T> action)
    {
        action(value);
        return value;
    }
}
