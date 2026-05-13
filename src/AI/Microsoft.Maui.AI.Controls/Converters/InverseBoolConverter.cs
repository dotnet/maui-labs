using System.Globalization;

namespace Microsoft.Maui.AI.Controls.Converters;

/// <summary>
/// Returns the inverse of a boolean value.
/// </summary>
internal sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : false;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : true;
}
