// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;

namespace Microsoft.Maui.AI.Chat.Controls;

// The renderings: the views of one block each, updated in place as the block changes (see MarkdownRenderer).
internal sealed partial class MarkdownRenderer
{
    /// <summary>The views of one block, kept up to date as the block changes.</summary>
    internal abstract class Rendering
    {
        private protected Rendering(MarkdownRenderer owner, RichTextNode node, BlockContext context, BlockKind kind)
        {
            Owner = owner;
            Node = node;
            Context = context;
            Kind = kind;
        }

        /// <summary>The node shown.</summary>
        internal RichTextNode Node { get; private set; }

        /// <summary>The view showing the node, or <see langword="null"/> when it shows nothing.</summary>
        internal abstract View? View { get; }

        private protected MarkdownRenderer Owner { get; }

        private protected BlockContext Context { get; }

        private protected BlockKind Kind { get; }

        /// <summary>Shows <paramref name="node"/>: this rendering, updated, or a new one when it cannot change to it.</summary>
        internal Rendering Update(RichTextNode node) => Update(node, Context);

        internal Rendering Update(RichTextNode node, BlockContext context)
        {
            if (context == Context
                && Classify(node, context) == Kind
                && (RichTextNodeComparer.AreEqual(Node, node) || TryUpdate(node)))
            {
                Node = node;
                return this;
            }

            return Owner.Render(node, context);
        }

        /// <summary>Changes the views to show <paramref name="node"/>, a node of the same kind.</summary>
        /// <returns><see langword="false"/> when only new views can show it.</returns>
        private protected abstract bool TryUpdate(RichTextNode node);
    }

    /// <summary>A view that never changes: a rule, an image, or nothing.</summary>
    private sealed class StaticRendering(MarkdownRenderer owner, RichTextNode node, BlockContext context, BlockKind kind, View? view)
        : Rendering(owner, node, context, kind)
    {
        internal override View? View => view;

        private protected override bool TryUpdate(RichTextNode node) => false;
    }

    /// <summary>One label: a paragraph, heading, raw HTML, a lone inline, or the plain text of a block nested too deep.</summary>
    private sealed class TextRendering : Rendering
    {
        private readonly Label _label;

        internal TextRendering(MarkdownRenderer owner, RichTextNode node, BlockContext context, BlockKind kind)
            : base(owner, node, context, kind) =>
            _label = kind switch
            {
                BlockKind.Plain => owner.CreateTextLabel(GetPlainText(node), context.TextStyleKey),
                BlockKind.Heading => owner.RenderHeading((HeadingNode)node),
                BlockKind.Literal => RenderLiteral(((HtmlNode)node).Value),
                BlockKind.Inline => owner.CreateInlineLabel([node], context.TextStyleKey, owner.FontSize, FontAttributes.None),
                _ => owner.CreateInlineLabel(node.Children, context.TextStyleKey, owner.FontSize, FontAttributes.None),
            };

        internal override View View => _label;

        private protected override bool TryUpdate(RichTextNode node)
        {
            switch (Kind)
            {
                case BlockKind.Plain:
                    _label.Text = GetPlainText(node);
                    return true;
                case BlockKind.Heading:
                    // The level sets the size, margin and semantics; a heading only grows within its level.
                    var level = GetHeadingLevel((HeadingNode)node);
                    if (level != GetHeadingLevel((HeadingNode)Node))
                        return false;
                    Owner.FillInlineLabel(_label, node.Children, Owner.GetHeadingFontSize(level), FontAttributes.Bold);
                    return true;
                case BlockKind.Literal:
                    _label.Text = ((HtmlNode)node).Value;
                    return true;
                case BlockKind.Inline:
                    Owner.FillInlineLabel(_label, [node], Owner.FontSize, FontAttributes.None);
                    return true;
                default:
                    Owner.FillInlineLabel(_label, node.Children, Owner.FontSize, FontAttributes.None);
                    return true;
            }
        }
    }

    /// <summary>A code block, or a table too big for a view per cell shown as text the same way.</summary>
    private sealed class CodeRendering : Rendering
    {
        private readonly Border _border;
        private readonly Label _code;
        private readonly Label? _language;

        internal CodeRendering(MarkdownRenderer owner, RichTextNode node, BlockContext context, BlockKind kind)
            : base(owner, node, context, kind) =>
            _border = RenderCodeBlock(GetCode(node), GetLanguage(node), out _code, out _language);

        internal override View View => _border;

        private protected override bool TryUpdate(RichTextNode node)
        {
            // The caption follows the info string as it streams in; a caption that comes or goes needs new views.
            var language = GetLanguage(node);
            if (string.IsNullOrWhiteSpace(language) != _language is null)
                return false;

            if (_language is not null)
                _language.Text = language;
            _code.Text = GetCode(node);
            return true;
        }

        private static string GetCode(RichTextNode node) =>
            node is CodeBlockNode code ? code.Code : GetTableText((TableNode)node);

        private static string? GetLanguage(RichTextNode node) => (node as CodeBlockNode)?.Language;
    }

    /// <summary>
    /// A stack of parts: the children of a list item, quote or other container (runs of inlines share a label), or a
    /// paragraph split around its images, which cannot live inside formatted text.
    /// </summary>
    private sealed class PartsRendering : Rendering
    {
        private readonly List<Rendering> _parts = new();
        private readonly List<View> _views = new();
        private VerticalStackLayout? _stack;
        private View? _view;

        internal PartsRendering(MarkdownRenderer owner, RichTextNode node, BlockContext context, BlockKind kind)
            : base(owner, node, context, kind) =>
            SetChildren(node.Children);

        internal override View? View => _view;

        private protected override bool TryUpdate(RichTextNode node)
        {
            SetChildren(node.Children);
            return true;
        }

        /// <summary>Shows <paramref name="children"/>, keeping the parts that did not change.</summary>
        internal void SetChildren(IReadOnlyList<RichTextNode> children)
        {
            var parts = Kind == BlockKind.ParagraphWithImages ? SplitParagraph(children) : SplitContainer(children);
            for (var i = 0; i < parts.Count; i++)
            {
                var (node, context) = parts[i];
                if (i < _parts.Count)
                    _parts[i] = _parts[i].Update(node, context);
                else
                    _parts.Add(Owner.Render(node, context));
            }

            if (_parts.Count > parts.Count)
                _parts.RemoveRange(parts.Count, _parts.Count - parts.Count);

            _views.Clear();
            foreach (var part in _parts)
            {
                if (part.View is { } view)
                    _views.Add(view);
            }

            if (_views.Count > 1)
            {
                _stack ??= new ChatVerticalStackLayout { Spacing = ContainerSpacing };
                SyncChildren(_stack, _views);
                _view = _stack;
                return;
            }

            // One part shows on its own, without a stack around it.
            _stack?.Clear();
            _stack = null;
            _view = _views.Count == 0 ? null : _views[0];
        }

        private List<(RichTextNode Node, BlockContext Context)> SplitContainer(IReadOnlyList<RichTextNode> children)
        {
            var parts = new List<(RichTextNode, BlockContext)>();
            ParagraphNode? run = null;
            foreach (var child in children)
            {
                if (IsInline(child))
                {
                    (run ??= new ParagraphNode()).AddChild(child);
                    continue;
                }

                if (run is not null)
                    parts.Add((run, Context));
                run = null;
                parts.Add((child, Context.Nested()));
            }

            if (run is not null)
                parts.Add((run, Context));
            return parts;
        }

        private List<(RichTextNode Node, BlockContext Context)> SplitParagraph(IReadOnlyList<RichTextNode> inlines)
        {
            var parts = new List<(RichTextNode, BlockContext)>();
            var run = new ParagraphNode();
            foreach (var inline in inlines)
            {
                if (inline is not (ImageNode or ImageReferenceNode))
                {
                    run.AddChild(inline);
                    continue;
                }

                if (IsVisible(run.Children))
                    parts.Add((run, Context));
                run = new ParagraphNode();
                parts.Add((inline, Context));
            }

            if (IsVisible(run.Children))
                parts.Add((run, Context));
            return parts;
        }

        // Line breaks and spaces between images do not need a label of their own.
        private static bool IsVisible(IReadOnlyList<RichTextNode> run)
        {
            foreach (var node in run)
            {
                if (node switch
                {
                    LineBreakNode => false,
                    TextNode text => !string.IsNullOrWhiteSpace(text.Text),
                    _ => true,
                })
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>A quote (a bar beside its content) or a footnote definition (its label beside its content).</summary>
    private sealed class FramedRendering : Rendering
    {
        private readonly Grid _grid;
        private readonly PartsRendering _content;
        private readonly string _placeholderStyleKey;
        private Label? _placeholder;
        private View _shown;

        internal FramedRendering(MarkdownRenderer owner, RichTextNode node, BlockContext context, BlockKind kind)
            : base(owner, node, context, kind)
        {
            View marker;
            BlockContext contentContext;
            if (kind == BlockKind.Quote)
            {
                // The style sets the bar's width; the bar stretches to the height of the quote.
                marker = Styled(new LineView(), AIChatThemeKeys.MarkdownBlockQuoteBarStyle);
                contentContext = context.Nested() with { TextStyleKey = AIChatThemeKeys.MarkdownBlockQuoteTextStyle };
                _placeholderStyleKey = AIChatThemeKeys.MarkdownBlockQuoteTextStyle;
            }
            else
            {
                marker = owner.CreateTextLabel("[" + ((FootnoteDefinitionNode)node).Label + "]", AIChatThemeKeys.MarkdownListMarkerStyle);
                contentContext = context.Nested();
                _placeholderStyleKey = context.TextStyleKey;
            }

            _content = new PartsRendering(owner, node, contentContext, BlockKind.Container);
            _grid = new ChatGrid
            {
                ColumnSpacing = kind == BlockKind.Quote ? 10 : 6,
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Star),
                },
            };
            _grid.Add(marker, 0, 0);
            _shown = GetContentView();
            _grid.Add(_shown, 1, 0);
        }

        internal override View View => _grid;

        private protected override bool TryUpdate(RichTextNode node)
        {
            if (node is FootnoteDefinitionNode footnote && footnote.Label != ((FootnoteDefinitionNode)Node).Label)
                return false;

            _content.SetChildren(node.Children);
            var view = GetContentView();
            if (!ReferenceEquals(view, _shown))
            {
                if (ReferenceEquals(_shown.Parent, _grid))
                    _grid.Remove(_shown);
                Detach(view);
                _grid.Add(view, 1, 0);
                _shown = view;
            }

            return true;
        }

        private View GetContentView() =>
            _content.View ?? (_placeholder ??= Owner.CreateTextLabel(string.Empty, _placeholderStyleKey));
    }

    /// <summary>A list: one grid keeps every item's content aligned after the widest marker.</summary>
    private sealed class ListRendering : Rendering
    {
        private readonly Grid _grid;
        private readonly BlockContext _itemContext;
        private readonly List<Label> _markers = new();
        private readonly List<Rendering> _items = new();
        private readonly List<View> _shown = new();
        private readonly List<Label?> _placeholders = new();

        internal ListRendering(MarkdownRenderer owner, ListNode list, BlockContext context)
            : base(owner, list, context, BlockKind.List)
        {
            _grid = new ChatGrid
            {
                ColumnSpacing = 8,
                RowSpacing = 4,
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Star),
                },
            };
            _itemContext = context.Nested() with { ListDepth = context.ListDepth + 1 };
            SetItems(list);
        }

        internal override View View => _grid;

        private protected override bool TryUpdate(RichTextNode node)
        {
            SetItems((ListNode)node);
            return true;
        }

        // The grid holds marker and content of each row in turn, so a row's content is child 2 × row + 1.
        private void SetItems(ListNode list)
        {
            var children = list.Children;
            var number = list.Start ?? 1;
            for (var row = 0; row < children.Count; row++, number++)
            {
                var child = children[row];
                var markerText = (child as ListItemNode)?.Checked switch
                {
                    true => CheckedBox,
                    false => UncheckedBox,
                    null when list.Ordered => number.ToString(CultureInfo.CurrentCulture) + ".",
                    _ => s_bullets[Context.ListDepth % s_bullets.Length],
                };

                if (row < _items.Count)
                {
                    if (_markers[row].Text != markerText)
                        _markers[row].Text = markerText;
                    _items[row] = _items[row].Update(child, _itemContext);
                    Show(row);
                    continue;
                }

                var marker = Styled(
                    new ChatLabel { Text = markerText, FontSize = Owner.FontSize, HorizontalTextAlignment = TextAlignment.End },
                    AIChatThemeKeys.MarkdownListMarkerStyle);
                var item = Owner.Render(child, _itemContext);
                _markers.Add(marker);
                _items.Add(item);
                _placeholders.Add(null);
                var content = GetContentView(row);
                _shown.Add(content);
                _grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                _grid.Add(marker, 0, row);
                _grid.Add(content, 1, row);
            }

            for (var row = _items.Count - 1; row >= children.Count; row--)
            {
                _grid.Remove(_markers[row]);
                if (ReferenceEquals(_shown[row].Parent, _grid))
                    _grid.Remove(_shown[row]);
                _grid.RowDefinitions.RemoveAt(row);
                _markers.RemoveAt(row);
                _items.RemoveAt(row);
                _shown.RemoveAt(row);
                _placeholders.RemoveAt(row);
            }
        }

        private void Show(int row)
        {
            var view = GetContentView(row);
            var shown = _shown[row];
            if (ReferenceEquals(view, shown))
                return;

            // The old content may have moved into the new one (an item's text into a stack with a nested list).
            if (ReferenceEquals(shown.Parent, _grid))
                _grid.Remove(shown);
            Detach(view);
            Grid.SetRow(view, row);
            Grid.SetColumn(view, 1);
            _grid.Insert((2 * row) + 1, view);
            _shown[row] = view;
        }

        private View GetContentView(int row) =>
            _items[row].View ?? (_placeholders[row] ??= Owner.CreateTextLabel(string.Empty, Context.TextStyleKey));
    }

    /// <summary>A table. Cells sit in even grid tracks; the odd tracks hold one-pixel lines.</summary>
    /// <remarks>Cells stay transparent, so the table blends with any bubble color.</remarks>
    private sealed class TableRendering : Rendering
    {
        private readonly Grid _grid;
        private readonly ScrollView _view;
        private readonly int _columns;
        private readonly List<TableRowNode> _rows = new();
        private readonly List<Label[]> _cells = new();
        private readonly List<View> _rowLines = new();
        private readonly List<View> _columnLines = new();

        private TableRendering(MarkdownRenderer owner, TableNode table, BlockContext context, List<TableRowNode> rows)
            : base(owner, table, context, BlockKind.Table)
        {
            _columns = GetColumnCount(table, rows);
            _grid = new ChatGrid { RowSpacing = 0, ColumnSpacing = 0 };
            for (var column = 0; column < _columns; column++)
            {
                if (column > 0)
                {
                    _grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1)));
                    var line = CreateTableLine(horizontal: false);
                    _grid.Add(line, (2 * column) - 1, 0);
                    _columnLines.Add(line);
                }

                _grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            }

            SetRows(table, rows);
            var border = Styled(new ChatBorder { Content = _grid, HorizontalOptions = LayoutOptions.Start }, AIChatThemeKeys.MarkdownTableBorderStyle);
            _view = CreateSideScroller(border);
        }

        internal override View View => _view;

        internal static Rendering Create(MarkdownRenderer owner, TableNode table, BlockContext context)
        {
            var rows = GetRows(table);
            return rows.Count == 0
                ? new StaticRendering(owner, table, context, BlockKind.Table, view: null)
                : new TableRendering(owner, table, context, rows);
        }

        private protected override bool TryUpdate(RichTextNode node)
        {
            var table = (TableNode)node;
            var rows = GetRows(table);
            if (rows.Count == 0 || GetColumnCount(table, rows) != _columns)
                return false;

            for (var column = 0; column < _columns; column++)
            {
                if (GetAlignment(table, column) != GetAlignment((TableNode)Node, column))
                    return false;
            }

            SetRows(table, rows);
            return true;
        }

        private void SetRows(TableNode table, List<TableRowNode> rows)
        {
            for (var row = 0; row < rows.Count; row++)
            {
                if (row < _rows.Count)
                {
                    if (!RichTextNodeComparer.AreEqual(_rows[row], rows[row]))
                    {
                        for (var column = 0; column < _columns; column++)
                            Owner.FillInlineLabel(_cells[row][column], GetCell(rows[row], column), Owner.FontSize, GetCellAttributes(row));
                    }

                    _rows[row] = rows[row];
                    continue;
                }

                if (row > 0)
                {
                    _grid.RowDefinitions.Add(new RowDefinition(new GridLength(1)));
                    var line = CreateTableLine(horizontal: true);
                    _grid.Add(line, 0, (2 * row) - 1);
                    Grid.SetColumnSpan(line, (2 * _columns) - 1);
                    _rowLines.Add(line);
                }

                _grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                var cells = new Label[_columns];
                for (var column = 0; column < _columns; column++)
                {
                    var label = Owner.CreateInlineLabel(
                        GetCell(rows[row], column),
                        row == 0 ? AIChatThemeKeys.MarkdownTableHeaderCellStyle : AIChatThemeKeys.MarkdownTableCellStyle,
                        Owner.FontSize,
                        GetCellAttributes(row));
                    label.Padding = new Thickness(10, 6);
                    label.MaximumWidthRequest = Owner.FontSize * 20;
                    label.HorizontalTextAlignment = GetAlignment(table, column);
                    _grid.Add(label, 2 * column, 2 * row);
                    cells[column] = label;
                }

                _cells.Add(cells);
                _rows.Add(rows[row]);
            }

            for (var row = _rows.Count - 1; row >= rows.Count; row--)
            {
                foreach (var cell in _cells[row])
                    _grid.Remove(cell);
                _grid.RowDefinitions.RemoveAt(_grid.RowDefinitions.Count - 1);
                if (row > 0)
                {
                    _grid.Remove(_rowLines[row - 1]);
                    _rowLines.RemoveAt(row - 1);
                    _grid.RowDefinitions.RemoveAt(_grid.RowDefinitions.Count - 1);
                }

                _cells.RemoveAt(row);
                _rows.RemoveAt(row);
            }

            foreach (var line in _columnLines)
                Grid.SetRowSpan(line, (2 * _rows.Count) - 1);
        }

        private static List<TableRowNode> GetRows(TableNode table) => table.Children.OfType<TableRowNode>().ToList();

        private static int GetColumnCount(TableNode table, List<TableRowNode> rows) =>
            Math.Max(1, Math.Max(rows.Max(static row => row.Children.Count), table.Alignment.Count));

        private static IReadOnlyList<RichTextNode> GetCell(TableRowNode row, int column) =>
            column < row.Children.Count ? row.Children[column].Children : [];

        private static FontAttributes GetCellAttributes(int row) => row == 0 ? FontAttributes.Bold : FontAttributes.None;
    }

    // ── Views ───────────────────────────────────────────────────────────

    /// <summary>Makes <paramref name="views"/> the children of <paramref name="host"/>, in order.</summary>
    /// <remarks>Children that stay are not touched; a view that lives in another layout moves here.</remarks>
    internal static void SyncChildren(Layout host, IReadOnlyList<View> views)
    {
        var same = 0;
        while (same < views.Count && same < host.Count && ReferenceEquals(host[same], views[same]))
            same++;
        if (same == views.Count && same == host.Count)
            return;

        var shown = new HashSet<View>(ReferenceEqualityComparer.Instance);
        for (var i = same; i < views.Count; i++)
            shown.Add(views[i]);
        for (var i = host.Count - 1; i >= same; i--)
        {
            if (host[i] is not View child || !shown.Contains(child))
                host.RemoveAt(i);
        }

        for (var i = same; i < views.Count; i++)
        {
            var view = views[i];
            if (i < host.Count && ReferenceEquals(host[i], view))
                continue;
            Detach(view);
            host.Insert(i, view);
        }
    }

    // A view moves between layouts as parts regroup; it must leave one before it joins another.
    private static void Detach(View view)
    {
        if (view.Parent is Layout layout)
            layout.Remove(view);
    }
}
