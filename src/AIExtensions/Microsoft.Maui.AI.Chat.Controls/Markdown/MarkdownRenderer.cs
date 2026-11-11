// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;
using System.Text;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>Builds native MAUI views for rich-text blocks. Used by <see cref="MarkdownView"/>.</summary>
/// <remarks>
/// Colors never come from code: every element takes a style from <see cref="MarkdownThemeKeys"/>
/// as a dynamic resource, with the matching <see cref="MarkdownTheme"/> style as the fallback.
/// </remarks>
internal sealed class MarkdownRenderer
{
    private const double CodeScale = 0.9;
    private const double CaptionScale = 0.8;
    private const double ContainerSpacing = 6;
    private const double MaximumImageHeight = 320;
    private const int MaxRenderDepth = 32;

    // Heading sizes relative to the body font size (h1 … h6).
    private static readonly double[] s_headingScales = [1.5, 1.3, 1.15, 1.05, 1.0, 0.95];

    // Bullets cycle with nesting depth; U+FE0E keeps the task boxes in text (not emoji) style.
    private static readonly string[] s_bullets = ["•", "◦", "▪"];
    private const string CheckedBox = "\u2611\uFE0E";
    private const string UncheckedBox = "\u2610\uFE0E";

    private readonly Command<string> _linkCommand;
    private Dictionary<string, DefinitionNode> _definitions = new(StringComparer.OrdinalIgnoreCase);

    internal MarkdownRenderer(Func<string?, Task<bool>> openLink)
    {
        ArgumentNullException.ThrowIfNull(openLink);
        _linkCommand = new Command<string>(url => _ = openLink(url));
        MonospaceFontFamily = GetMonospaceFontFamily(GetCurrentPlatform());
    }

    internal double FontSize { get; set; } = 15;

    internal string MonospaceFontFamily { get; }

    internal View? RenderBlock(RichTextNode node) =>
        RenderBlock(node, new BlockContext(MarkdownThemeKeys.TextStyle, ListDepth: 0, Depth: 0));

    // ── Theme ───────────────────────────────────────────────────────────

    /// <summary>Applies a theme style key, falling back to the default theme's style when the key is missing.</summary>
    internal static void ApplyStyle(Element target, BindableProperty styleProperty, string key) =>
        ApplyThemeValue(target, styleProperty, key);

    /// <summary>Binds <paramref name="property"/> to a theme key with the default theme value as fallback.</summary>
    internal static void ApplyThemeValue(Element target, BindableProperty property, string key)
    {
        // A binding has lower precedence than a dynamic resource, so the default only shows
        // while no ancestor (or the application) provides the key.
        if (MarkdownTheme.Defaults.TryGetValue(key, out var fallback))
            target.SetAppTheme(property, fallback, fallback);
        target.SetDynamicResource(property, key);
    }

    internal static string GetMonospaceFontFamily(DevicePlatform platform)
    {
        if (platform == DevicePlatform.iOS
            || platform == DevicePlatform.MacCatalyst
            || platform == DevicePlatform.macOS
            || platform == DevicePlatform.tvOS)
        {
            return "Menlo";
        }

        if (platform == DevicePlatform.WinUI)
            return "Consolas";

        // Android, and fontconfig on Linux, resolve the generic family.
        return "monospace";
    }

    private static DevicePlatform GetCurrentPlatform()
    {
        try
        {
            return DeviceInfo.Platform;
        }
        catch (Exception)
        {
            return DevicePlatform.Unknown;
        }
    }

    // ── Links ───────────────────────────────────────────────────────────

    internal static bool TryCreateLinkUri(string? value, out Uri uri) =>
        TryCreateUri(value, allowMailTo: true, out uri);

    internal static bool TryCreateImageUri(string? value, out Uri uri) =>
        TryCreateUri(value, allowMailTo: false, out uri);

    private static bool TryCreateUri(string? value, bool allowMailTo, out Uri uri)
    {
        if (!string.IsNullOrWhiteSpace(value)
            && Uri.TryCreate(value, UriKind.Absolute, out var candidate)
            && (candidate.Scheme == Uri.UriSchemeHttp
                || candidate.Scheme == Uri.UriSchemeHttps
                || (allowMailTo && candidate.Scheme == Uri.UriSchemeMailto)))
        {
            uri = candidate;
            return true;
        }

        uri = null!;
        return false;
    }

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
            ImageNode image => RenderImage(image.Url, image.Alt, image.Title, context),
            ImageReferenceNode reference => RenderImageReference(reference, context),
            HtmlNode html => RenderLiteral(html.Value, context),
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
            var image = inline is ImageNode node
                ? RenderImage(node.Url, node.Alt, node.Title, context)
                : RenderImageReference((ImageReferenceNode)inline, context);
            if (image is not null)
                views.Add(image);
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
            MarkdownThemeKeys.HeadingStyle,
            FontSize * s_headingScales[level - 1],
            FontAttributes.Bold);
        label.Margin = new Thickness(0, level <= 2 ? 4 : 2, 0, 0);
        SemanticProperties.SetHeadingLevel(label, (SemanticHeadingLevel)level);
        return label;
    }

    private Border RenderCodeBlock(string code, string? language)
    {
        var text = new Label
        {
            Text = code,
            FontFamily = MonospaceFontFamily,
            FontSize = FontSize * CodeScale,
            LineBreakMode = LineBreakMode.NoWrap,
        };
        ApplyStyle(text, VisualElement.StyleProperty, MarkdownThemeKeys.CodeBlockTextStyle);

        // Code keeps its line structure; long lines scroll sideways instead of wrapping.
        var scroller = new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Default,
            Content = text,
        };

        var border = new Border();
        ApplyStyle(border, VisualElement.StyleProperty, MarkdownThemeKeys.CodeBlockStyle);
        if (string.IsNullOrWhiteSpace(language))
        {
            border.Content = scroller;
            return border;
        }

        var caption = new Label
        {
            Text = language,
            FontSize = FontSize * CaptionScale,
            LineBreakMode = LineBreakMode.TailTruncation,
        };
        ApplyStyle(caption, VisualElement.StyleProperty, MarkdownThemeKeys.CodeBlockLanguageStyle);
        border.Content = new VerticalStackLayout { Spacing = 4, Children = { caption, scroller } };
        return border;
    }

    private Grid RenderBlockQuote(BlockQuoteNode quote, BlockContext context)
    {
        var bar = new BoxView
        {
            WidthRequest = 3,
            HeightRequest = 0,
            VerticalOptions = LayoutOptions.Fill,
        };
        ApplyStyle(bar, VisualElement.StyleProperty, MarkdownThemeKeys.BlockQuoteBarStyle);

        var content = RenderContainer(
                quote.Children,
                context.Nested() with { TextStyleKey = MarkdownThemeKeys.BlockQuoteTextStyle })
            ?? CreateTextLabel(string.Empty, MarkdownThemeKeys.BlockQuoteTextStyle);

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

            var marker = new Label
            {
                Text = markerText,
                FontSize = FontSize,
                HorizontalTextAlignment = TextAlignment.End,
            };
            ApplyStyle(marker, VisualElement.StyleProperty, MarkdownThemeKeys.ListMarkerStyle);

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
                    isHeader ? MarkdownThemeKeys.TableHeaderCellStyle : MarkdownThemeKeys.TableCellStyle,
                    FontSize,
                    isHeader ? FontAttributes.Bold : FontAttributes.None);
                label.Padding = new Thickness(10, 6);
                label.MaximumWidthRequest = FontSize * 20;
                label.HorizontalTextAlignment = GetAlignment(table, column);
                grid.Add(label, 2 * column, 2 * row);
            }
        }

        var border = new Border { Content = grid, HorizontalOptions = LayoutOptions.Start };
        ApplyStyle(border, VisualElement.StyleProperty, MarkdownThemeKeys.TableBorderStyle);
        return new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = border };
    }

    private static BoxView CreateTableLine(bool horizontal)
    {
        // Zero requests stop the BoxView's 40x40 default size from widening auto tracks.
        var line = horizontal
            ? new BoxView { HeightRequest = 1, WidthRequest = 0 }
            : new BoxView { WidthRequest = 1, HeightRequest = 0 };
        ApplyStyle(line, VisualElement.StyleProperty, MarkdownThemeKeys.TableLineStyle);
        return line;
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

    private static BoxView RenderThematicBreak()
    {
        var rule = new BoxView
        {
            HeightRequest = 1,
            HorizontalOptions = LayoutOptions.Fill,
            Margin = new Thickness(0, 4),
        };
        ApplyStyle(rule, VisualElement.StyleProperty, MarkdownThemeKeys.ThematicBreakStyle);
        return rule;
    }

    private View? RenderImage(string url, string? alt, string? title, BlockContext context)
    {
        var description = string.IsNullOrWhiteSpace(alt) ? title : alt;
        if (!TryCreateImageUri(url, out var uri))
        {
            // Only http(s) images load; anything else shows its alt text.
            return string.IsNullOrWhiteSpace(description) ? null : CreateTextLabel(description, context.TextStyleKey);
        }

        var image = new Image
        {
            Source = ImageSource.FromUri(uri),
            Aspect = Aspect.AspectFit,
            HorizontalOptions = LayoutOptions.Start,
            MaximumHeightRequest = MaximumImageHeight,
        };
        if (!string.IsNullOrWhiteSpace(description))
            SemanticProperties.SetDescription(image, description);
        return image;
    }

    private View? RenderImageReference(ImageReferenceNode reference, BlockContext context) =>
        _definitions.TryGetValue(reference.Label, out var definition)
            ? RenderImage(definition.Url, reference.Alt, definition.Title, context)
            : CreateTextLabel(reference.Alt ?? "[" + reference.Label + "]", context.TextStyleKey);

    private Label RenderLiteral(string text, BlockContext context)
    {
        var label = CreateTextLabel(text, context.TextStyleKey);
        label.FontFamily = MonospaceFontFamily;
        label.FontSize = FontSize * CodeScale;
        return label;
    }

    private Grid RenderFootnoteDefinition(FootnoteDefinitionNode footnote, BlockContext context)
    {
        var marker = CreateTextLabel("[" + footnote.Label + "]", MarkdownThemeKeys.ListMarkerStyle);
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

    private Label CreateTextLabel(string text, string styleKey)
    {
        var label = new Label
        {
            Text = text,
            FontSize = FontSize,
            LineBreakMode = LineBreakMode.WordWrap,
        };
        ApplyStyle(label, VisualElement.StyleProperty, styleKey);
        return label;
    }

    private Label CreateInlineLabel(
        IReadOnlyList<RichTextNode> nodes,
        string styleKey,
        double fontSize,
        FontAttributes baseAttributes)
    {
        var label = new Label
        {
            FontSize = fontSize,
            LineBreakMode = LineBreakMode.WordWrap,
        };
        ApplyStyle(label, VisualElement.StyleProperty, styleKey);

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
            ApplyStyle(span, Span.StyleProperty, MarkdownThemeKeys.LinkStyle);
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

        var span = new Span
        {
            Text = code,
            FontFamily = MonospaceFontFamily,
            FontSize = state.FontSize * CodeScale,
        };
        if (state.Attributes != FontAttributes.None)
            span.FontAttributes = state.Attributes;
        ApplyStyle(span, Span.StyleProperty, MarkdownThemeKeys.InlineCodeStyle);

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
