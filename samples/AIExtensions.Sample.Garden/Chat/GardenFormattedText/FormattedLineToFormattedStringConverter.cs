using System.Globalization;

namespace AIExtensions.Sample.Garden.Chat;

/// <summary>
/// Converts a parsed <see cref="FormattedLine"/> into a MAUI <see cref="FormattedString"/>. This is the
/// one piece that can't be pure XAML: MAUI has no markup for building a run of inline spans with mixed
/// bold/italic/code from data. Line kind is encoded in the spans themselves — a heading enlarges/bolds
/// every span; a bullet gets a leading "• " span — so the XAML item template is a single
/// <see cref="Label"/> bound to <see cref="Label.FormattedText"/>.
/// </summary>
public sealed class FormattedLineToFormattedStringConverter : IValueConverter
{
    private const double HeadingFontSize = 18;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not FormattedLine line)
            return null;

        var isHeading = line.Kind == FormattedLineKind.Heading;
        var formatted = new FormattedString();

        if (line.Kind == FormattedLineKind.Bullet)
        {
            formatted.Spans.Add(new Span { Text = "\u2022  ", FontAttributes = FontAttributes.Bold });
        }

        foreach (var span in line.Spans)
        {
            var run = new Span { Text = span.Text };

            var attributes = FontAttributes.None;
            if (span.Bold || isHeading)
                attributes |= FontAttributes.Bold;
            if (span.Italic)
                attributes |= FontAttributes.Italic;
            run.FontAttributes = attributes;

            if (isHeading)
                run.FontSize = HeadingFontSize;

            if (span.Code)
            {
                run.FontFamily = "Courier New";
                run.BackgroundColor = Colors.LightGray;
            }

            formatted.Spans.Add(run);
        }

        return formatted;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
