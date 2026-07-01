using Microsoft.Maui.AI.Chat.Controls;

namespace AiControlsSample;

/// <summary>
/// Renders a <see cref="FormattedTextBlock"/>. All parsing happens in the handler,
/// so the view only walks the pre-parsed <see cref="FormattedLine"/> model and builds
/// MAUI <see cref="FormattedString"/> spans.
/// </summary>
public sealed class FormattedTextView : ContentContextView
{
    private readonly VerticalStackLayout _layout;

    public FormattedTextView()
    {
        _layout = new VerticalStackLayout { Spacing = 4, Padding = new Thickness(12, 8) };
        Content = _layout;
    }

    protected override void RefreshFromContentContext()
    {
        _layout.Children.Clear();

        if (ContentContext?.Block is not FormattedTextBlock block)
            return;

        foreach (var line in block.Lines)
        {
            _layout.Children.Add(RenderLine(line));
        }
    }

    private static View RenderLine(FormattedLine line)
    {
        var label = new Label
        {
            FormattedText = BuildFormatted(line.Spans),
            LineBreakMode = LineBreakMode.WordWrap,
        };

        switch (line.Kind)
        {
            case FormattedLineKind.Heading:
                label.FontSize = 18;
                label.FontAttributes = FontAttributes.Bold;
                return label;

            case FormattedLineKind.Bullet:
                var row = new HorizontalStackLayout { Spacing = 6 };
                row.Add(new Label { Text = "•", FontAttributes = FontAttributes.Bold });
                label.HorizontalOptions = LayoutOptions.Fill;
                row.Add(label);
                return row;

            default:
                return label;
        }
    }

    private static FormattedString BuildFormatted(IReadOnlyList<FormattedSpan> spans)
    {
        var fs = new FormattedString();
        foreach (var s in spans)
        {
            var span = new Span { Text = s.Text };

            if (s.Bold && s.Italic)
                span.FontAttributes = FontAttributes.Bold | FontAttributes.Italic;
            else if (s.Bold)
                span.FontAttributes = FontAttributes.Bold;
            else if (s.Italic)
                span.FontAttributes = FontAttributes.Italic;

            if (s.Code)
            {
                span.FontFamily = "Courier New";
                span.BackgroundColor = Colors.LightGray;
            }

            fs.Spans.Add(span);
        }
        return fs;
    }
}
