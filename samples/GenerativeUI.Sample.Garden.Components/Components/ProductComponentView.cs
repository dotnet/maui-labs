using System.Globalization;
using Microsoft.Maui.AI.GenerativeUI.Binding;
using Microsoft.Maui.AI.GenerativeUI.Composition;

namespace GenerativeUI.Sample.Garden.Components;

public abstract class ProductComponentView : ContentView, ICompositionComponent
{
    public string? Variant { get; private set; }

    public void ApplyVariant(string? variant)
    {
        if (string.Equals(Variant, variant, StringComparison.OrdinalIgnoreCase))
            return;

        Variant = variant;
        OnVariantChanged();
    }

    protected virtual void OnVariantChanged()
    {
    }

    protected static Microsoft.Maui.Controls.Binding Bind(
        string path,
        IValueConverter? converter = null,
        object? converterParameter = null)
    {
        var binding = UiBindingCompiler.Compile(path, converter: converter);
        binding.ConverterParameter = converterParameter;
        return binding;
    }
}

internal static class GardenComponentVisuals
{
    public static Color PrimaryText { get; } = Color.FromArgb("#173C34");
    public static Color SecondaryText { get; } = Color.FromArgb("#5D7268");
    public static Color Primary { get; } = Color.FromArgb("#2F7D5B");
    public static Color Accent { get; } = Color.FromArgb("#C89B3C");
    public static Color Stroke { get; } = Color.FromArgb("#C9DDD0");
    public static Color CardBackground { get; } = Color.FromArgb("#FFFFFF");

    public static Border Card(string automationId, View content, double padding = 16)
        => new()
        {
            AutomationId = automationId,
            Padding = padding,
            BackgroundColor = CardBackground,
            Stroke = new SolidColorBrush(Stroke),
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 20 },
            Content = content,
        };

    public static Label SectionTitle(string automationId, string text)
    {
        var label = new Label
        {
            AutomationId = automationId,
            Text = text,
            FontSize = 20,
            FontAttributes = FontAttributes.Bold,
            TextColor = PrimaryText,
        };
        SemanticProperties.SetHeadingLevel(label, SemanticHeadingLevel.Level2);
        return label;
    }
}

internal sealed class CurrencyValueConverter : IValueConverter
{
    public static CurrencyValueConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            double number => number.ToString("C2", CultureInfo.GetCultureInfo("en-US")),
            decimal number => number.ToString("C2", CultureInfo.GetCultureInfo("en-US")),
            IFormattable number => number.ToString("C2", CultureInfo.GetCultureInfo("en-US")),
            _ => "",
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

internal sealed class PrefixedValueConverter : IValueConverter
{
    public static PrefixedValueConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => $"{parameter}{value}";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

internal sealed class StockValueConverter : IValueConverter
{
    public static StockValueConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is null ? "Stock not tracked" : $"{value} in stock";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

internal sealed class InvariantValueConverter : IValueConverter
{
    public static InvariantValueConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is IFormattable formattable
            ? formattable.ToString(null, CultureInfo.InvariantCulture)
            : value?.ToString() ?? "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

internal sealed class HexColorValueConverter : IValueConverter
{
    public static HexColorValueConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = value?.ToString();
        return string.IsNullOrWhiteSpace(text) ? Colors.Transparent : Color.FromArgb(text);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
