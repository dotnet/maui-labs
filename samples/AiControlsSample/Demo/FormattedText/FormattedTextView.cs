using Microsoft.Maui.AI.Chat.Controls;
using Microsoft.Maui.AI.Chat.Controls.Themes;
using Microsoft.Maui.Controls.Shapes;

namespace AiControlsSample;

/// <summary>
/// Renders a <see cref="FormattedTextBlock"/>. All parsing happens in the handler,
/// so the view only walks the pre-parsed <see cref="FormattedLine"/> model and builds
/// MAUI <see cref="FormattedString"/> spans.
/// </summary>
/// <remarks>
/// The content is wrapped in an assistant-styled speech bubble (using the chat theme's
/// colour and sizing tokens) so formatted responses look like normal assistant messages.
/// </remarks>
public sealed class FormattedTextView : ContentContextView
{
    private readonly VerticalStackLayout _layout;

    public FormattedTextView()
    {
        _layout = new VerticalStackLayout { Spacing = 4 };

        var bubble = new Border
        {
            Padding = new Thickness(12, 10),
            HorizontalOptions = LayoutOptions.Start,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(16, 16, 16, 4) },
            Content = _layout,
        };
        bubble.SetDynamicResource(BackgroundColorProperty, ChatThemeKeys.AssistantBackground);
        bubble.SetDynamicResource(MaximumWidthRequestProperty, ChatThemeKeys.BubbleMaxWidth);

        Content = new Grid { Padding = new Thickness(0, 4), Children = { bubble } };
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
        label.SetDynamicResource(Label.TextColorProperty, ChatThemeKeys.AssistantTextColor);

        switch (line.Kind)
        {
            case FormattedLineKind.Heading:
                label.FontSize = 18;
                label.FontAttributes = FontAttributes.Bold;
                return label;

            case FormattedLineKind.Bullet:
                // Use a Grid (not HorizontalStackLayout) so the star column stays
                // Auto-width and the text column takes the remaining space and wraps.
                var row = new Grid
                {
                    ColumnDefinitions =
                    {
                        new ColumnDefinition(GridLength.Auto),
                        new ColumnDefinition(GridLength.Star),
                    },
                    ColumnSpacing = 6,
                };
                var bullet = new Label { Text = "•", FontAttributes = FontAttributes.Bold };
                bullet.SetDynamicResource(Label.TextColorProperty, ChatThemeKeys.AssistantTextColor);
                row.Add(bullet, 0, 0);
                row.Add(label, 1, 0);
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
