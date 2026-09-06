using System.Globalization;

namespace CopilotSdkSample;

public sealed class BoolToItalicConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? FontAttributes.Italic : FontAttributes.None;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
