using System.Globalization;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>
/// Turns a <see cref="DocumentDiff"/> into text with its changes marked: added words on green, removed words on red
/// and struck through.
/// </summary>
public sealed class DiffFormattedStringConverter : IValueConverter
{
    // Translucent, so the marks read on the light and the dark background alike.
    private static readonly Color AddedBackground = Color.FromArgb("#4D22C55E");
    private static readonly Color RemovedBackground = Color.FromArgb("#40EF4444");

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = new FormattedString();
        if (value is not IEnumerable<DiffSegment> segments)
            return text;

        foreach (var segment in segments)
        {
            var span = new Span { Text = segment.Text };
            switch (segment.Kind)
            {
                case DiffKind.Added:
                    span.BackgroundColor = AddedBackground;
                    break;
                case DiffKind.Removed:
                    span.BackgroundColor = RemovedBackground;
                    span.TextDecorations = TextDecorations.Strikethrough;
                    break;
            }

            text.Spans.Add(span);
        }

        return text;
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
