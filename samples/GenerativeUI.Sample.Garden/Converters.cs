using System.Globalization;

namespace GenerativeUI.Sample.Garden;

/// <summary>Maps a <c>ChatMessageKind</c>-derived boolean to a horizontal alignment.</summary>
public sealed class BoolToEndAlignmentConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? LayoutOptions.End : LayoutOptions.Start;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>True when the bound string is non-empty (used to collapse empty tool detail).</summary>
public sealed class StringNotEmptyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => !string.IsNullOrEmpty(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
