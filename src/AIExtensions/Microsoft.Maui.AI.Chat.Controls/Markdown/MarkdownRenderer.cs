// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;
using System.Text;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>Builds native MAUI views for rich-text blocks. Used by <see cref="MarkdownView"/>.</summary>
/// <remarks>
/// Colors and fonts never come from code: every element takes a chat theme style (see <see cref="AIChatThemeKeys"/>)
/// as a dynamic resource. Only sizes are set here, from <see cref="FontSize"/> and <see cref="CodeFontSize"/>, so
/// headings and inline code scale with the text around them.
/// </remarks>
internal sealed class MarkdownRenderer
{
    private const double ContainerSpacing = 6;
    private const double MaximumImageHeight = 320;
    private const double MaximumImageWidth = 480;
    private const int MaxRenderDepth = 32;

    // Heading sizes relative to the body font size (h1 … h6).
    private static readonly double[] s_headingScales = [1.5, 1.3, 1.15, 1.05, 1.0, 0.95];

    // Bullets cycle with nesting depth; U+FE0E keeps the task boxes in text (not emoji) style.
    private static readonly string[] s_bullets = ["•", "◦", "▪"];
    private const string CheckedBox = "\u2611\uFE0E";
    private const string UncheckedBox = "\u2610\uFE0E";

    private static readonly ScrollBarVisibility s_sideScrollBarVisibility = GetSideScrollBarVisibility(AIChatTheme.GetPlatform());

    private readonly Command<string> _linkCommand;
    private Dictionary<string, DefinitionNode> _definitions = new(StringComparer.OrdinalIgnoreCase);

    internal MarkdownRenderer(Func<string?, Task<bool>> openLink)
    {
        ArgumentNullException.ThrowIfNull(openLink);
        _linkCommand = new Command<string>(url => _ = openLink(url));
    }

    /// <summary>The body font size; headings scale from it.</summary>
    internal double FontSize { get; set; } = 15;

    /// <summary>The font size of code in body text; inline code in a heading keeps the same ratio.</summary>
    internal double CodeFontSize { get; set; } = 13;

    internal View? RenderBlock(RichTextNode node) =>
        RenderBlock(node, new BlockContext(AIChatThemeKeys.AssistantTextStyle, ListDepth: 0, Depth: 0));

    // ── Theme ───────────────────────────────────────────────────────────

    private static T Styled<T>(T element, string key)
        where T : VisualElement =>
        element.Styled(key);

    private static void Styled(Span span, string key) =>
        span.SetDynamicResource(Span.StyleProperty, key);

    // ── Links ───────────────────────────────────────────────────────────

    /// <summary>Gets the link to open for <paramref name="value"/>: http, https, and mailto only.</summary>
    internal static bool TryCreateLinkUri(string? value, out Uri uri)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var candidate) && ChatVisuals.IsSafeUri(candidate))
        {
            uri = candidate;
            return true;
        }

        uri = null!;
        return false;
    }

    /// <summary>Gets the image to load for <paramref name="value"/>: http and https only.</summary>
    internal static bool TryCreateImageUri(string? value, out Uri uri) =>
        TryCreateLinkUri(value, out uri) && uri.Scheme != Uri.UriSchemeMailto;

    // ── Reference definitions ───────────────────────────────────────────

    /// <summary>Replaces the link reference definitions; returns <see langword="false"/> when they changed.</summary>
    internal bool UpdateDefinitions(IReadOnlyList<RichTextNode> document)
    {
        var definitions = new Dictionary<string, DefinitionNode>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<RichTextNode>(document);
        while (pending.Count > 0)
        {
            var node = pending.Pop();
            if (node is DefinitionNode { Label.Length: > 0 } definition)
                definitions.TryAdd(definition.Label, definition);
            foreach (var child in node.Children)
                pending.Push(child);
        }

        var unchanged = definitions.Count == _definitions.Count;
        if (unchanged)
        {
            foreach (var (label, definition) in definitions)
            {
                if (!_definitions.TryGetValue(label, out var previous)
                    || !RichTextNodeComparer.AreEqual(previous, definition))
                {
                    unchanged = false;
                    break;
                }
            }
        }

        _definitions = definitions;
        return unchanged;
    }

    // ── Blocks ──────────────────────────────────────────────────────────

    private readonly record struct BlockContext(string TextStyleKey, int ListDepth, int Depth)
    {
        public BlockContext Nested() => this with { Depth = Depth + 1 };
    }

    private View? RenderBlock(RichTextNode node, BlockContext context)
    {
        if (context.Depth > MaxRenderDepth)
            return CreateTextLabel(GetPlainText(node), context.TextStyleKey);

        return node switch
        {
            ParagraphNode => RenderParagraph(node.Children, context),
            HeadingNode heading => RenderHeading(heading),
            CodeBlockNode code => RenderCodeBlock(code.Code, code.Language),
            BlockQuoteNode quote => RenderBlockQuote(quote, context),
            ListNode list => RenderList(list, context),
            TableNode table => RenderTable(table),
            ThematicBreakNode => RenderThematicBreak(),
            ImageNode image => RenderImage(image.Url, image.Alt, image.Title),
            ImageReferenceNode reference => RenderImageReference(reference, context),
            HtmlNode html => RenderLiteral(html.Value),
            DefinitionNode => null,
            FootnoteDefinitionNode footnote => RenderFootnoteDefinition(footnote, context),
            _ when IsInline(node) => CreateInlineLabel([node], context.TextStyleKey, FontSize, FontAttributes.None),
            _ => RenderContainer(node.Children, context),
        };
    }

    // Children of a container (quote, list item, footnote): runs of inline nodes share a label.
    private View? RenderContainer(IReadOnlyList<RichTextNode> children, BlockContext context)
    {
        var views = new List<View>();
        var inlineRun = new List<RichTextNode>();
        foreach (var child in children)
        {
            if (IsInline(child))
            {
                inlineRun.Add(child);
                continue;
            }

            FlushInlineRun();
            if (RenderBlock(child, context.Nested()) is { } view)
                views.Add(view);
        }

        FlushInlineRun();
        return Stack(views, ContainerSpacing);

        void FlushInlineRun()
        {
            if (inlineRun.Count == 0)
                return;
            if (RenderParagraph(inlineRun.ToArray(), context) is { } view)
                views.Add(view);
            inlineRun.Clear();
        }
    }

    private View? RenderParagraph(IReadOnlyList<RichTextNode> inlines, BlockContext context)
    {
        if (!inlines.Any(static node => node is ImageNode or ImageReferenceNode))
            return CreateInlineLabel(inlines, context.TextStyleKey, FontSize, FontAttributes.None);

        // Images cannot live inside formatted text, so the paragraph splits around them.
        var views = new List<View>();
        var run = new List<RichTextNode>();
        foreach (var inline in inlines)
        {
            if (inline is not (ImageNode or ImageReferenceNode))
            {
                run.Add(inline);
                continue;
            }

            FlushRun();
            views.Add(inline is ImageNode node
                ? RenderImage(node.Url, node.Alt, node.Title)
                : RenderImageReference((ImageReferenceNode)inline, context));
        }

        FlushRun();
        return Stack(views, ContainerSpacing);

        void FlushRun()
        {
            var visible = run.Exists(static node => node switch
            {
                LineBreakNode => false,
                TextNode text => !string.IsNullOrWhiteSpace(text.Text),
                _ => true,
            });
            if (visible)
                views.Add(CreateInlineLabel(run.ToArray(), context.TextStyleKey, FontSize, FontAttributes.None));
            run.Clear();
        }
    }

    private Label RenderHeading(HeadingNode heading)
    {
        var level = Math.Clamp(heading.Level, 1, 6);
        var label = CreateInlineLabel(
            heading.Children,
            AIChatThemeKeys.MarkdownHeadingStyle,
            FontSize * s_headingScales[level - 1],
            FontAttributes.Bold);
        label.Margin = new Thickness(0, level <= 2 ? 4 : 2, 0, 0);
        SemanticProperties.SetHeadingLevel(label, (SemanticHeadingLevel)level);
        return label;
    }

    private static Border RenderCodeBlock(string code, string? language)
    {
        // The chat's code styles, so code looks the same in answers and in tool cards.
        var text = Styled(new Label { Text = code }, AIChatThemeKeys.CodeTextStyle);
        text.LineBreakMode = LineBreakMode.NoWrap;

        // Code keeps its line structure; long lines scroll sideways instead of wrapping.
        var scroller = CreateSideScroller(text);

        var border = Styled(new Border(), AIChatThemeKeys.CodeBlockStyle);
        if (string.IsNullOrWhiteSpace(language))
        {
            border.Content = scroller;
            return border;
        }

        var caption = Styled(new Label { Text = language }, AIChatThemeKeys.MarkdownCodeBlockLanguageStyle);
        border.Content = new VerticalStackLayout { Spacing = 4, Children = { caption, scroller } };
        return border;
    }

    private Grid RenderBlockQuote(BlockQuoteNode quote, BlockContext context)
    {
        // The style sets the bar's width; the bar stretches to the height of the quote.
        var bar = Styled(new LineView(), AIChatThemeKeys.MarkdownBlockQuoteBarStyle);

        var content = RenderContainer(
                quote.Children,
                context.Nested() with { TextStyleKey = AIChatThemeKeys.MarkdownBlockQuoteTextStyle })
            ?? CreateTextLabel(string.Empty, AIChatThemeKeys.MarkdownBlockQuoteTextStyle);

        var grid = new Grid
        {
            ColumnSpacing = 10,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
            },
        };
        grid.Add(bar, 0, 0);
        grid.Add(content, 1, 0);
        return grid;
    }

    private Grid RenderList(ListNode list, BlockContext context)
    {
        // One grid per list keeps every item's content aligned after the widest marker.
        var grid = new Grid
        {
            ColumnSpacing = 8,
            RowSpacing = 4,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
            },
        };

        var itemContext = context.Nested() with { ListDepth = context.ListDepth + 1 };
        var number = list.Start ?? 1;
        var row = 0;
        foreach (var child in list.Children)
        {
            var item = child as ListItemNode;
            var markerText = item?.Checked switch
            {
                true => CheckedBox,
                false => UncheckedBox,
                null when list.Ordered => number.ToString(CultureInfo.CurrentCulture) + ".",
                _ => s_bullets[context.ListDepth % s_bullets.Length],
            };

            var marker = Styled(
                new Label { Text = markerText, FontSize = FontSize, HorizontalTextAlignment = TextAlignment.End },
                AIChatThemeKeys.MarkdownListMarkerStyle);

            var content = (item is null ? RenderBlock(child, itemContext) : RenderContainer(item.Children, itemContext))
                ?? CreateTextLabel(string.Empty, context.TextStyleKey);

            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.Add(marker, 0, row);
            grid.Add(content, 1, row);
            row++;
            number++;
        }

        return grid;
    }

    private ScrollView? RenderTable(TableNode table)
    {
        var rows = table.Children.OfType<TableRowNode>().ToList();
        if (rows.Count == 0)
            return null;

        var columns = Math.Max(1, Math.Max(rows.Max(static row => row.Children.Count), table.Alignment.Count));

        // Cells sit in even grid tracks; the odd tracks hold one-pixel lines, so cells stay
        // transparent and the table blends with any bubble color.
        var grid = new Grid { RowSpacing = 0, ColumnSpacing = 0 };
        for (var column = 0; column < columns; column++)
        {
            if (column > 0)
                grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1)));
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        }

        for (var row = 0; row < rows.Count; row++)
        {
            if (row > 0)
                grid.RowDefinitions.Add(new RowDefinition(new GridLength(1)));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        }

        for (var row = 1; row < rows.Count; row++)
        {
            var line = CreateTableLine(horizontal: true);
            grid.Add(line, 0, (2 * row) - 1);
            Grid.SetColumnSpan(line, (2 * columns) - 1);
        }

        for (var column = 1; column < columns; column++)
        {
            var line = CreateTableLine(horizontal: false);
            grid.Add(line, (2 * column) - 1, 0);
            Grid.SetRowSpan(line, (2 * rows.Count) - 1);
        }

        for (var row = 0; row < rows.Count; row++)
        {
            var cells = rows[row].Children;
            var isHeader = row == 0;
            for (var column = 0; column < columns; column++)
            {
                var label = CreateInlineLabel(
                    column < cells.Count ? cells[column].Children : [],
                    isHeader ? AIChatThemeKeys.MarkdownTableHeaderCellStyle : AIChatThemeKeys.MarkdownTableCellStyle,
                    FontSize,
                    isHeader ? FontAttributes.Bold : FontAttributes.None);
                label.Padding = new Thickness(10, 6);
                label.MaximumWidthRequest = FontSize * 20;
                label.HorizontalTextAlignment = GetAlignment(table, column);
                grid.Add(label, 2 * column, 2 * row);
            }
        }

        var border = Styled(new Border { Content = grid, HorizontalOptions = LayoutOptions.Start }, AIChatThemeKeys.MarkdownTableBorderStyle);
        return CreateSideScroller(border);
    }

    private static ScrollView CreateSideScroller(View content) => new()
    {
        Orientation = ScrollOrientation.Horizontal,
        HorizontalScrollBarVisibility = s_sideScrollBarVisibility,
        Content = content,
    };

    /// <summary>
    /// Gets the scroll bar of code and tables that scroll sideways. Android flashes a new scroller's bar across its
    /// last line of text, and a swipe is how sideways content is found there anyway. Elsewhere the bar shows while
    /// scrolling, and a mouse needs it.
    /// </summary>
    internal static ScrollBarVisibility GetSideScrollBarVisibility(DevicePlatform platform) =>
        platform == DevicePlatform.Android ? ScrollBarVisibility.Never : ScrollBarVisibility.Default;

    private static BoxView CreateTableLine(bool horizontal) =>
        Styled(horizontal ? new LineView { HeightRequest = 1 } : new LineView { WidthRequest = 1 }, AIChatThemeKeys.MarkdownTableLineStyle);

    private static TextAlignment GetAlignment(TableNode table, int column) =>
        column < table.Alignment.Count
            ? table.Alignment[column] switch
            {
                TableColumnAlignment.Center => TextAlignment.Center,
                TableColumnAlignment.Right => TextAlignment.End,
                _ => TextAlignment.Start,
            }
            : TextAlignment.Start;

    // The style sets the rule's thickness.
    private static BoxView RenderThematicBreak() =>
        Styled(new LineView { Margin = new Thickness(0, 4) }, AIChatThemeKeys.MarkdownThematicBreakStyle);

    private static View RenderImage(string url, string? alt, string? title)
    {
        var description = string.IsNullOrWhiteSpace(alt) ? title : alt;

        // Only http(s) images load. Any other source (an attachment:// reference, a relative path), or an image that
        // fails to load, shows as a chip with its description instead of an empty gap.
        if (!TryCreateImageUri(url, out var uri))
            return ChatVisuals.CreateImageChip(url, description);

        var image = new Image
        {
            Source = ImageSource.FromUri(uri),
            Aspect = Aspect.AspectFit,
            HorizontalOptions = LayoutOptions.Start,
            MaximumHeightRequest = MaximumImageHeight,
            MaximumWidthRequest = MaximumImageWidth,
        };
        SemanticProperties.SetDescription(image, string.IsNullOrWhiteSpace(description) ? "Image" : description);
        return new LinkedImageView(image, () => ChatVisuals.CreateImageChip(url, description));
    }

    private View RenderImageReference(ImageReferenceNode reference, BlockContext context) =>
        _definitions.TryGetValue(reference.Label, out var definition)
            ? RenderImage(definition.Url, reference.Alt, definition.Title)
            : CreateTextLabel(reference.Alt ?? "[" + reference.Label + "]", context.TextStyleKey);

    // Raw HTML is shown as source text, in the code font.
    private static Label RenderLiteral(string text) =>
        Styled(new Label { Text = text }, AIChatThemeKeys.CodeTextStyle);

    private Grid RenderFootnoteDefinition(FootnoteDefinitionNode footnote, BlockContext context)
    {
        var marker = CreateTextLabel("[" + footnote.Label + "]", AIChatThemeKeys.MarkdownListMarkerStyle);
        var content = RenderContainer(footnote.Children, context.Nested())
            ?? CreateTextLabel(string.Empty, context.TextStyleKey);
        var grid = new Grid
        {
            ColumnSpacing = 6,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
            },
        };
        grid.Add(marker, 0, 0);
        grid.Add(content, 1, 0);
        return grid;
    }

    private static View? Stack(List<View> views, double spacing)
    {
        if (views.Count <= 1)
            return views.Count == 0 ? null : views[0];

        var stack = new VerticalStackLayout { Spacing = spacing };
        foreach (var view in views)
            stack.Children.Add(view);
        return stack;
    }

    // ── Inlines ─────────────────────────────────────────────────────────

    private readonly record struct InlineState(
        FontAttributes Attributes,
        TextDecorations Decorations,
        string? LinkUrl,
        double FontSize);

    private Label CreateTextLabel(string text, string styleKey) =>
        Styled(new Label { Text = text, FontSize = FontSize, LineBreakMode = LineBreakMode.WordWrap }, styleKey);

    private Label CreateInlineLabel(
        IReadOnlyList<RichTextNode> nodes,
        string styleKey,
        double fontSize,
        FontAttributes baseAttributes)
    {
        var label = Styled(new Label { FontSize = fontSize, LineBreakMode = LineBreakMode.WordWrap }, styleKey);

        if (nodes.Count == 0)
        {
            label.Text = string.Empty;
        }
        else if (nodes.Count == 1 && nodes[0] is TextNode plain)
        {
            label.Text = plain.Text;
        }
        else
        {
            var formatted = new FormattedString();
            var state = new InlineState(baseAttributes, TextDecorations.None, LinkUrl: null, fontSize);
            foreach (var node in nodes)
                AppendInline(formatted, node, state, depth: 0);
            label.FormattedText = formatted;
        }

        return label;
    }

    private void AppendInline(FormattedString formatted, RichTextNode node, InlineState state, int depth)
    {
        if (depth > MaxRenderDepth)
        {
            AddSpan(formatted, GetPlainText(node), state);
            return;
        }

        switch (node)
        {
            case TextNode text:
                AddSpan(formatted, text.Text, state);
                break;
            case InlineCodeNode code:
                AddCodeSpan(formatted, code.Code, state);
                break;
            case LineBreakNode:
                formatted.Spans.Add(new Span { Text = "\n" });
                break;
            case StrongNode:
                AppendChildren(formatted, node, state with { Attributes = state.Attributes | FontAttributes.Bold }, depth);
                break;
            case EmphasisNode:
                AppendChildren(formatted, node, state with { Attributes = state.Attributes | FontAttributes.Italic }, depth);
                break;
            case StrikethroughNode:
                AppendChildren(formatted, node, state with { Decorations = state.Decorations | TextDecorations.Strikethrough }, depth);
                break;
            case LinkNode link:
                AppendLink(formatted, link.Url, link, state, depth);
                break;
            case LinkReferenceNode reference when _definitions.TryGetValue(reference.Label, out var definition):
                AppendLink(formatted, definition.Url, reference, state, depth);
                break;
            case LinkReferenceNode reference when reference.Children.Count == 0:
                AddSpan(formatted, "[" + reference.Label + "]", state);
                break;
            case ImageNode image:
                AddSpan(formatted, image.Alt ?? image.Title ?? string.Empty, state);
                break;
            case ImageReferenceNode image:
                AddSpan(formatted, image.Alt ?? image.Label, state);
                break;
            case FootnoteReferenceNode footnote:
                AddSpan(formatted, "[" + footnote.Label + "]", state);
                break;
            case HtmlNode html:
                AddSpan(formatted, html.Value, state);
                break;
            default:
                AppendChildren(formatted, node, state, depth);
                break;
        }
    }

    private void AppendChildren(FormattedString formatted, RichTextNode node, InlineState state, int depth)
    {
        foreach (var child in node.Children)
            AppendInline(formatted, child, state, depth + 1);
    }

    private void AppendLink(FormattedString formatted, string url, RichTextNode link, InlineState state, int depth)
    {
        // Only http, https and mailto links become tappable; anything else reads as plain text.
        var linkState = TryCreateLinkUri(url, out _) ? state with { LinkUrl = url } : state;
        if (link.Children.Count == 0)
            AddSpan(formatted, url, linkState);
        else
            AppendChildren(formatted, link, linkState, depth);
    }

    private void AddSpan(FormattedString formatted, string text, InlineState state)
    {
        if (text.Length == 0)
            return;

        var span = new Span { Text = text };
        if (state.Attributes != FontAttributes.None)
            span.FontAttributes = state.Attributes;

        var decorations = state.Decorations;
        if (state.LinkUrl is not null)
        {
            Styled(span, AIChatThemeKeys.LinkSpanStyle);
            AddLinkGesture(span, state.LinkUrl);
            // A local decoration replaces the style's underline, so keep it explicitly.
            if (decorations != TextDecorations.None)
                decorations |= TextDecorations.Underline;
        }

        if (decorations != TextDecorations.None)
            span.TextDecorations = decorations;
        formatted.Spans.Add(span);
    }

    private void AddCodeSpan(FormattedString formatted, string code, InlineState state)
    {
        if (code.Length == 0)
            return;

        // The style sets the code font; the size keeps the theme's code-to-body ratio, also inside headings.
        var span = new Span { Text = code, FontSize = FontSize > 0 ? state.FontSize * CodeFontSize / FontSize : CodeFontSize };
        if (state.Attributes != FontAttributes.None)
            span.FontAttributes = state.Attributes;
        Styled(span, AIChatThemeKeys.MarkdownInlineCodeStyle);

        var decorations = state.Decorations;
        if (state.LinkUrl is not null)
        {
            AddLinkGesture(span, state.LinkUrl);
            decorations |= TextDecorations.Underline;
        }

        if (decorations != TextDecorations.None)
            span.TextDecorations = decorations;
        formatted.Spans.Add(span);
    }

    private void AddLinkGesture(Span span, string url) =>
        span.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = _linkCommand,
            CommandParameter = url,
        });

    // ── Helpers ─────────────────────────────────────────────────────────

    /// <summary>A rule, bar, or grid line: it fills its row or column across, and asks only for its thickness.</summary>
    /// <remarks>
    /// A plain <see cref="BoxView"/> asks for 40 × 40 without a size request, which would widen auto table columns and
    /// pad short quotes, and a zero size request would stop it from stretching at all.
    /// </remarks>
    private sealed class LineView : BoxView
    {
        protected override Size MeasureOverride(double widthConstraint, double heightConstraint) =>
            new(Math.Max(0, WidthRequest) + Margin.HorizontalThickness, Math.Max(0, HeightRequest) + Margin.VerticalThickness);
    }

    private static bool IsInline(RichTextNode node) =>
        node is TextNode
            or InlineCodeNode
            or LineBreakNode
            or StrongNode
            or EmphasisNode
            or StrikethroughNode
            or LinkNode
            or LinkReferenceNode
            or FootnoteReferenceNode
            or ImageNode
            or ImageReferenceNode;

    private static string GetPlainText(RichTextNode node)
    {
        var builder = new StringBuilder();
        var pending = new Stack<RichTextNode>();
        pending.Push(node);
        while (pending.Count > 0)
        {
            switch (pending.Pop())
            {
                case TextNode text:
                    builder.Append(text.Text);
                    break;
                case InlineCodeNode code:
                    builder.Append(code.Code);
                    break;
                case CodeBlockNode code:
                    builder.Append(code.Code).Append('\n');
                    break;
                case LineBreakNode:
                    builder.Append('\n');
                    break;
                case var container:
                    for (var i = container.Children.Count - 1; i >= 0; i--)
                        pending.Push(container.Children[i]);
                    break;
            }
        }

        return builder.ToString();
    }
}
