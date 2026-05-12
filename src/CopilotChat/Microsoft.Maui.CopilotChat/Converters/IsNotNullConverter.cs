using System.Globalization;

namespace Microsoft.Maui.CopilotChat.Converters;

/// <summary>
/// Returns true if the value is not null (and not empty for strings).
/// </summary>
internal sealed class IsNotNullConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string s ? !string.IsNullOrEmpty(s) : value is not null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
