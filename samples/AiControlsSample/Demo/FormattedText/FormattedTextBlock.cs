using System.Text;
using Microsoft.Maui.AI.Chat;

namespace AiControlsSample;

/// <summary>
/// A sample rich-text block. This is NOT a library feature — it demonstrates the
/// correct pattern for building rich text: a custom block whose
/// <see cref="FormattedTextHandler"/> parses the raw Microsoft.Extensions.AI text into
/// a structured model, so the view just renders (no parsing at render time).
/// </summary>
/// <remarks>
/// It derives from <see cref="TextContentBlock"/> so a basic text template (e.g. the
/// right-hand raw view in the sample) still renders it as plain <see cref="TextContentBlock.RawText"/>,
/// while a richer template can bind to the parsed <see cref="Lines"/>.
/// </remarks>
public sealed class FormattedTextBlock : TextContentBlock
{
    /// <summary>The parsed lines, rebuilt as text streams in.</summary>
    public IReadOnlyList<FormattedLine> Lines { get; private set; } = [];

    public override void AppendText(string text)
    {
        base.AppendText(text);
        Lines = FormattedTextParser.Parse(RawText);
    }
}

public enum FormattedLineKind
{
    Paragraph,
    Bullet,
    Heading,
}

public sealed record FormattedSpan(string Text, bool Bold, bool Italic, bool Code);

public sealed record FormattedLine(FormattedLineKind Kind, IReadOnlyList<FormattedSpan> Spans);

/// <summary>
/// A deliberately tiny Markdown-ish parser: <c># heading</c>, <c>- bullet</c>,
/// <c>**bold**</c>, <c>*italic*</c>, and <c>`code`</c>.
/// </summary>
internal static class FormattedTextParser
{
    public static IReadOnlyList<FormattedLine> Parse(string text)
    {
        var lines = new List<FormattedLine>();
        foreach (var rawLine in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.TrimEnd();
            if (line.Length == 0)
                continue;

            if (line.StartsWith("# "))
                lines.Add(new FormattedLine(FormattedLineKind.Heading, ParseInline(line[2..])));
            else if (line.StartsWith("- ") || line.StartsWith("* "))
                lines.Add(new FormattedLine(FormattedLineKind.Bullet, ParseInline(line[2..])));
            else
                lines.Add(new FormattedLine(FormattedLineKind.Paragraph, ParseInline(line)));
        }
        return lines;
    }

    private static IReadOnlyList<FormattedSpan> ParseInline(string text)
    {
        var spans = new List<FormattedSpan>();
        var plain = new StringBuilder();
        var i = 0;

        void FlushPlain()
        {
            if (plain.Length > 0)
            {
                spans.Add(new FormattedSpan(plain.ToString(), false, false, false));
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
                    spans.Add(new FormattedSpan(text[(i + 2)..close], Bold: true, Italic: false, Code: false));
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
                    spans.Add(new FormattedSpan(text[(i + 1)..close], Bold: false, Italic: true, Code: false));
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
                    spans.Add(new FormattedSpan(text[(i + 1)..close], Bold: false, Italic: false, Code: true));
                    i = close + 1;
                    continue;
                }
            }

            plain.Append(text[i]);
            i++;
        }

        FlushPlain();
        return spans;
    }
}
