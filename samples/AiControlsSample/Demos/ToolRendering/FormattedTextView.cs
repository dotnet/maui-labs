using Microsoft.Maui.AI.Chat;
using Microsoft.Maui.AI.Chat.Controls;

namespace AiControlsSample;

/// <summary>
/// A deliberately tiny "fancy plain text" formatter for assistant messages.
/// <para>
/// This is NOT a library feature — it's a sample demonstrating what you can build
/// on top of a plain <see cref="RichContentBlock"/>. It renders a small subset of
/// Markdown-ish syntax: <c>**bold**</c>, <c>*italic*</c>, <c>`code`</c>, and
/// <c>- </c> bullet lines.
/// </para>
/// </summary>
public class FormattedTextView : ContentContextView
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

        var text = ContentContext?.Block is RichContentBlock rcb ? rcb.RawText : string.Empty;
        if (string.IsNullOrEmpty(text))
            return;

        foreach (var rawLine in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.TrimEnd();
            if (line.Length == 0)
                continue;

            if (line.StartsWith("- ") || line.StartsWith("* "))
            {
                _layout.Children.Add(BulletRow(line[2..]));
            }
            else if (line.StartsWith("# "))
            {
                _layout.Children.Add(new Label
                {
                    Text = line[2..],
                    FontSize = 18,
                    FontAttributes = FontAttributes.Bold,
                    LineBreakMode = LineBreakMode.WordWrap,
                });
            }
            else
            {
                _layout.Children.Add(new Label
                {
                    FormattedText = BuildInline(line),
                    LineBreakMode = LineBreakMode.WordWrap,
                });
            }
        }
    }

    private static View BulletRow(string content)
    {
        var row = new HorizontalStackLayout { Spacing = 6 };
        row.Add(new Label { Text = "•", FontAttributes = FontAttributes.Bold });
        row.Add(new Label
        {
            FormattedText = BuildInline(content),
            LineBreakMode = LineBreakMode.WordWrap,
            HorizontalOptions = LayoutOptions.Fill,
        });
        return row;
    }

    // Minimal inline parser for **bold**, *italic*, and `code`.
    private static FormattedString BuildInline(string text)
    {
        var fs = new FormattedString();
        var plain = new System.Text.StringBuilder();
        var i = 0;

        void FlushPlain()
        {
            if (plain.Length > 0)
            {
                fs.Spans.Add(new Span { Text = plain.ToString() });
                plain.Clear();
            }
        }

        while (i < text.Length)
        {
            if (i + 1 < text.Length && text[i] == '*' && text[i + 1] == '*')
            {
                var close = text.IndexOf("**", i + 2, StringComparison.Ordinal);
                if (close > 0)
                {
                    FlushPlain();
                    fs.Spans.Add(new Span { Text = text[(i + 2)..close], FontAttributes = FontAttributes.Bold });
                    i = close + 2;
                    continue;
                }
            }
            else if (text[i] == '*')
            {
                var close = text.IndexOf('*', i + 1);
                if (close > 0)
                {
                    FlushPlain();
                    fs.Spans.Add(new Span { Text = text[(i + 1)..close], FontAttributes = FontAttributes.Italic });
                    i = close + 1;
                    continue;
                }
            }
            else if (text[i] == '`')
            {
                var close = text.IndexOf('`', i + 1);
                if (close > 0)
                {
                    FlushPlain();
                    fs.Spans.Add(new Span
                    {
                        Text = text[(i + 1)..close],
                        FontFamily = "Courier New",
                        BackgroundColor = Colors.LightGray,
                    });
                    i = close + 1;
                    continue;
                }
            }

            plain.Append(text[i]);
            i++;
        }

        FlushPlain();
        return fs;
    }
}
