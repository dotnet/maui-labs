// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>Builds native MAUI views for rich-text blocks and keeps them up to date. Used by <see cref="MarkdownView"/>.</summary>
/// <remarks>
/// <para>
/// Colors and fonts never come from code: every element takes a chat theme style (see <see cref="AIChatThemeKeys"/>)
/// as a dynamic resource. Only sizes are set here, from <see cref="FontSize"/> and <see cref="CodeFontSize"/>, so
/// headings and inline code scale with the text around them.
/// </para>
/// <para>
/// A block renders to a <see cref="Rendering"/>, which holds its views and updates them for a new version of the
/// block. A streaming answer grows at its end, so an update keeps what did not change, refills the text of the
/// paragraph, heading or code that grew, and inside lists, quotes and tables touches only the items, rows and cells
/// that changed: the work follows the change, not the size of the block. An updated rendering shows exactly what a
/// fresh one would.
/// </para>
/// </remarks>
internal sealed partial class MarkdownRenderer
{
    private const double ContainerSpacing = 6;
    private const double MaximumImageHeight = 320;
    private const double MaximumImageWidth = 480;
    private const int MaxRenderDepth = 32;

    /// <summary>The most columns a table has and still gets a view per cell.</summary>
    /// <remarks>
    /// Laying out a view per cell takes seconds for thousands of columns, so a wider table, or one with more than
    /// <see cref="MaxTableCells"/> cells, shows as text instead: one row per line, cells separated by bars, in a block
    /// that scrolls sideways like code. No real answer comes near either limit.
    /// </remarks>
    internal const int MaxTableColumns = 64;

    /// <summary>The most cells a table has and still gets a view per cell (see <see cref="MaxTableColumns"/>).</summary>
    internal const int MaxTableCells = 2000;

    private static readonly BlockContext s_rootContext = new(AIChatThemeKeys.AssistantTextStyle, ListDepth: 0, Depth: 0);

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

    /// <summary>Renders a top-level block.</summary>
    internal Rendering Render(RichTextNode node) => Render(node, s_rootContext);

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

    internal readonly record struct BlockContext(string TextStyleKey, int ListDepth, int Depth)
    {
        public BlockContext Nested() => this with { Depth = Depth + 1 };
    }

    /// <summary>What a node renders as; a rendering only updates in place for a node of the same kind.</summary>
    internal enum BlockKind
    {
        Plain,
        Paragraph,
        ParagraphWithImages,
        Heading,
        Code,
        Quote,
        List,
        Table,
        TableText,
        Rule,
        Image,
        ImageReference,
        Literal,
        Definition,
        Footnote,
        Inline,
        Container,
    }

    private static BlockKind Classify(RichTextNode node, BlockContext context)
    {
        if (context.Depth > MaxRenderDepth)
            return BlockKind.Plain;

        return node switch
        {
            ParagraphNode => HasImages(node.Children) ? BlockKind.ParagraphWithImages : BlockKind.Paragraph,
            HeadingNode => BlockKind.Heading,
            CodeBlockNode => BlockKind.Code,
            BlockQuoteNode => BlockKind.Quote,
            ListNode => BlockKind.List,
            TableNode table => IsTooBigForCells(table) ? BlockKind.TableText : BlockKind.Table,
            ThematicBreakNode => BlockKind.Rule,
            ImageNode => BlockKind.Image,
            ImageReferenceNode => BlockKind.ImageReference,
            HtmlNode => BlockKind.Literal,
            DefinitionNode => BlockKind.Definition,
            FootnoteDefinitionNode => BlockKind.Footnote,
            _ when IsInline(node) => BlockKind.Inline,
            _ => BlockKind.Container,
        };
    }

    private Rendering Render(RichTextNode node, BlockContext context)
    {
        var kind = Classify(node, context);
        return kind switch
        {
            BlockKind.Plain or BlockKind.Paragraph or BlockKind.Heading or BlockKind.Literal or BlockKind.Inline
                => new TextRendering(this, node, context, kind),
            BlockKind.ParagraphWithImages or BlockKind.Container => new PartsRendering(this, node, context, kind),
            BlockKind.Code or BlockKind.TableText => new CodeRendering(this, node, context, kind),
            BlockKind.Quote or BlockKind.Footnote => new FramedRendering(this, node, context, kind),
            BlockKind.List => new ListRendering(this, (ListNode)node, context),
            BlockKind.Table => TableRendering.Create(this, (TableNode)node, context),
            BlockKind.Rule => new StaticRendering(this, node, context, kind, RenderThematicBreak()),
            BlockKind.Image when node is ImageNode image
                => new StaticRendering(this, node, context, kind, RenderImage(image.Url, image.Alt, image.Title)),
            BlockKind.ImageReference
                => new StaticRendering(this, node, context, kind, RenderImageReference((ImageReferenceNode)node, context)),
            _ => new StaticRendering(this, node, context, kind, view: null),
        };
    }

    private Label RenderHeading(HeadingNode heading)
    {
        var level = GetHeadingLevel(heading);
        var label = CreateInlineLabel(heading.Children, AIChatThemeKeys.MarkdownHeadingStyle, GetHeadingFontSize(level), FontAttributes.Bold);
        label.Margin = new Thickness(0, level <= 2 ? 4 : 2, 0, 0);
        SemanticProperties.SetHeadingLevel(label, (SemanticHeadingLevel)level);
        return label;
    }

    private static int GetHeadingLevel(HeadingNode heading) => Math.Clamp(heading.Level, 1, 6);

    private double GetHeadingFontSize(int level) => FontSize * s_headingScales[level - 1];

    private static Border RenderCodeBlock(string code, string? language, out Label text, out Label? caption)
    {
        // The chat's code styles, so code looks the same in answers and in tool cards.
        text = Styled(new ChatLabel { Text = code }, AIChatThemeKeys.CodeTextStyle);
        text.LineBreakMode = LineBreakMode.NoWrap;

        // Code keeps its line structure; long lines scroll sideways instead of wrapping.
        var scroller = CreateSideScroller(text);

        var border = Styled(new ChatBorder(), AIChatThemeKeys.CodeBlockStyle);
        if (string.IsNullOrWhiteSpace(language))
        {
            caption = null;
            border.Content = scroller;
            return border;
        }

        caption = Styled(new ChatLabel { Text = language }, AIChatThemeKeys.MarkdownCodeBlockLanguageStyle);
        border.Content = new ChatVerticalStackLayout { Spacing = 4, Children = { caption, scroller } };
        return border;
    }

    private static bool IsTooBigForCells(TableNode table)
    {
        var rows = 0;
        var columns = table.Alignment.Count;
        foreach (var child in table.Children)
        {
            if (child is TableRowNode row)
            {
                rows++;
                columns = Math.Max(columns, row.Children.Count);
            }
        }

        return columns > MaxTableColumns || (long)rows * columns > MaxTableCells;
    }

    // One row per line, cells separated by bars.
    private static string GetTableText(TableNode table)
    {
        var builder = new StringBuilder();
        var firstRow = true;
        foreach (var row in table.Children.OfType<TableRowNode>())
        {
            if (!firstRow)
                builder.Append('\n');
            firstRow = false;
            for (var column = 0; column < row.Children.Count; column++)
            {
                if (column > 0)
                    builder.Append(" | ");
                builder.Append(GetPlainText(row.Children[column]).ReplaceLineEndings(" "));
            }
        }

        return builder.ToString();
    }

    private static ScrollView CreateSideScroller(View content) => new ChatScrollView
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

        var image = new ChatImage
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
        Styled(new ChatLabel { Text = text }, AIChatThemeKeys.CodeTextStyle);

    private static bool HasImages(IReadOnlyList<RichTextNode> inlines)
    {
        foreach (var inline in inlines)
        {
            if (inline is ImageNode or ImageReferenceNode)
                return true;
        }

        return false;
    }

    // ── Inlines ─────────────────────────────────────────────────────────

    private readonly record struct InlineState(
        FontAttributes Attributes,
        TextDecorations Decorations,
        string? LinkUrl,
        double FontSize);

    private Label CreateTextLabel(string text, string styleKey) =>
        Styled(new ChatLabel { Text = text, FontSize = FontSize, LineBreakMode = LineBreakMode.WordWrap }, styleKey);

    private Label CreateInlineLabel(
        IReadOnlyList<RichTextNode> nodes,
        string styleKey,
        double fontSize,
        FontAttributes baseAttributes)
    {
        var label = Styled(new ChatLabel { FontSize = fontSize, LineBreakMode = LineBreakMode.WordWrap }, styleKey);
        FillInlineLabel(label, nodes, fontSize, baseAttributes);
        return label;
    }

    // Text and FormattedText replace each other, so a label can switch between plain and formatted in place.
    private void FillInlineLabel(Label label, IReadOnlyList<RichTextNode> nodes, double fontSize, FontAttributes baseAttributes)
    {
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
                formatted.Spans.Add(new ChatSpan { Text = "\n" });
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

        var span = new ChatSpan { Text = text };
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
        var span = new ChatSpan { Text = code, FontSize = FontSize > 0 ? state.FontSize * CodeFontSize / FontSize : CodeFontSize };
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
    internal sealed class LineView : BoxView
    {
        private readonly bool _themed = ChatElements.EnsureTheme();

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
