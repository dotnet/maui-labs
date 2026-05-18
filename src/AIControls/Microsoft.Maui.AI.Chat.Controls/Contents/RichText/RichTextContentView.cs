using Microsoft.AspNetCore.Components.AI;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>
/// Renders a <see cref="RichContentBlock"/> by walking the <see cref="RichTextNode"/>
/// AST and producing MAUI views for each node type. Falls back to raw text for
/// unsupported node types.
/// </summary>
public class RichTextContentView : ContentContextView
{
    private VerticalStackLayout? _layout;

    public RichTextContentView()
    {
        _layout = new VerticalStackLayout { Spacing = 4, Padding = new Thickness(12, 8) };
        Content = _layout;
    }

    protected override void RefreshFromContentContext()
    {
        if (_layout is null || ContentContext?.Block is not RichContentBlock rcb)
            return;

        _layout.Children.Clear();

        if (rcb.Content.Count == 0)
        {
            // Fallback to raw text
            _layout.Children.Add(new Label { Text = rcb.RawText, LineBreakMode = LineBreakMode.WordWrap });
            return;
        }

        foreach (var node in rcb.Content)
        {
            var view = RenderNode(node);
            if (view is not null)
                _layout.Children.Add(view);
        }
    }

    private static View? RenderNode(RichTextNode node) => node switch
    {
        ParagraphNode p => RenderParagraph(p),
        HeadingNode h => RenderHeading(h),
        CodeBlockNode cb => RenderCodeBlock(cb),
        ListNode l => RenderList(l),
        BlockQuoteNode bq => RenderBlockQuote(bq),
        ThematicBreakNode => new BoxView { HeightRequest = 1, Color = Colors.LightGray, Margin = new Thickness(0, 8) },
        TextNode t => new Label { Text = t.Text, LineBreakMode = LineBreakMode.WordWrap },
        _ => new Label { Text = $"[{node.GetType().Name}]", TextColor = Colors.Gray, FontSize = 10 },
    };

    private static View RenderParagraph(ParagraphNode p)
    {
        var text = ExtractInlineText(p.Children);
        return new Label { Text = text, LineBreakMode = LineBreakMode.WordWrap };
    }

    private static View RenderHeading(HeadingNode h)
    {
        var text = ExtractInlineText(h.Children);
        var fontSize = h.Level switch
        {
            1 => 24.0,
            2 => 20.0,
            3 => 18.0,
            _ => 16.0,
        };
        return new Label
        {
            Text = text,
            FontSize = fontSize,
            FontAttributes = FontAttributes.Bold,
            Margin = new Thickness(0, 4, 0, 2),
        };
    }

    private static View RenderCodeBlock(CodeBlockNode cb)
    {
        return new Border
        {
            BackgroundColor = Color.FromArgb("#F3F4F6"),
            Padding = new Thickness(12, 8),
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 6 },
            Stroke = Color.FromArgb("#E5E7EB"),
            Content = new Label
            {
                Text = cb.Code,
                FontFamily = "Courier New",
                FontSize = 12,
                LineBreakMode = LineBreakMode.WordWrap,
            },
        };
    }

    private static View RenderList(ListNode l)
    {
        var layout = new VerticalStackLayout { Spacing = 2, Margin = new Thickness(16, 4, 0, 4) };
        for (int i = 0; i < l.Children.Count; i++)
        {
            if (l.Children[i] is ListItemNode item)
            {
                var bullet = l.Ordered ? $"{i + 1}." : "•";
                var text = ExtractInlineText(item.Children);
                layout.Children.Add(new Label
                {
                    Text = $"{bullet} {text}",
                    LineBreakMode = LineBreakMode.WordWrap,
                });
            }
        }
        return layout;
    }

    private static View RenderBlockQuote(BlockQuoteNode bq)
    {
        var text = ExtractInlineText(bq.Children);
        return new Border
        {
            Padding = new Thickness(12, 8),
            Margin = new Thickness(8, 4),
            BackgroundColor = Color.FromArgb("#F9FAFB"),
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 4 },
            Stroke = Color.FromArgb("#6366F1"),
            StrokeThickness = 3,
            Content = new Label
            {
                Text = text,
                FontAttributes = FontAttributes.Italic,
                LineBreakMode = LineBreakMode.WordWrap,
            },
        };
    }

    private static string ExtractInlineText(IReadOnlyList<RichTextNode> children)
    {
        if (children.Count == 0)
            return string.Empty;

        var parts = new List<string>();
        foreach (var child in children)
        {
            parts.Add(child switch
            {
                TextNode t => t.Text,
                StrongNode s => ExtractInlineText(s.Children),
                EmphasisNode e => ExtractInlineText(e.Children),
                InlineCodeNode ic => ic.Code,
                LinkNode ln => ExtractInlineText(ln.Children),
                LineBreakNode => "\n",
                ParagraphNode p => ExtractInlineText(p.Children),
                ListItemNode li => ExtractInlineText(li.Children),
                _ => child.ToString() ?? string.Empty,
            });
        }
        return string.Join("", parts);
    }
}
