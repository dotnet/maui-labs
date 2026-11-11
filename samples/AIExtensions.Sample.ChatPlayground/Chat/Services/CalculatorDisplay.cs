using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Formats a <c>calculate</c> tool call as arithmetic, such as <c>12 × 7 = 84</c>.</summary>
public static class CalculatorDisplay
{
    /// <summary>Formats the call's operands and operator, such as <c>12 × 7</c>.</summary>
    /// <returns>The expression, or <see langword="null"/> while the arguments are incomplete.</returns>
    public static string? FormatExpression(IDictionary<string, object?>? arguments, CultureInfo? culture = null)
    {
        if (arguments is null ||
            !arguments.TryGetValue("left", out var left) ||
            !arguments.TryGetValue("operation", out var operation) ||
            !arguments.TryGetValue("right", out var right))
        {
            return null;
        }

        return $"{FormatValue(left, culture)} {FormatOperator(ReadString(operation))} {FormatValue(right, culture)}";
    }

    /// <summary>
    /// Formats a result that is a number, or returns <see langword="null"/> for none, a failure or anything else.
    /// </summary>
    public static string? FormatResult(FunctionResultContent? result, CultureInfo? culture = null) =>
        result is { Exception: null } && IsNumber(result.Result) ? FormatValue(result.Result, culture) : null;

    /// <summary>
    /// Describes a result that is not a number: why the call failed, or what came back instead, such as that the call
    /// never ran. Returns <see langword="null"/> for none or a number.
    /// </summary>
    public static string? FormatMessage(FunctionResultContent? result) => result switch
    {
        null => null,
        { Exception: { } exception } => exception.Message,
        { Result: var value } when IsNumber(value) => null,
        { Result: null or JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } } => "No result",
        { Result: JsonElement { ValueKind: JsonValueKind.String } element } => element.GetString(),
        { Result: JsonElement element } => element.GetRawText(),
        { Result: var value } => Convert.ToString(value, CultureInfo.CurrentCulture),
    };

    /// <summary>Gets the error message of a failed result, or <see langword="null"/> for a success.</summary>
    public static string? FormatError(FunctionResultContent? result)
    {
        if (result is null)
            return null;
        if (result.Exception is { } exception)
            return exception.Message;
        return IsError(result.Result) ? ReadString(result.Result) : null;
    }

    /// <summary>Gets the arithmetic symbol for an operation name such as <c>multiply</c>.</summary>
    public static string FormatOperator(string? operation) =>
        operation?.Trim().ToLowerInvariant() switch
        {
            "add" or "+" or "plus" => "+",
            "subtract" or "-" or "minus" => "\u2212",
            "multiply" or "*" or "x" or "times" => "\u00D7",
            "divide" or "/" => "\u00F7",
            { Length: > 0 } other => other,
            _ => "?",
        };

    private static string FormatValue(object? value, CultureInfo? culture)
    {
        culture ??= CultureInfo.CurrentCulture;
        return value switch
        {
            JsonElement { ValueKind: JsonValueKind.Number } element when element.TryGetDouble(out var number) =>
                FormatNumber(number, culture),
            JsonElement { ValueKind: JsonValueKind.String } element => FormatText(element.GetString(), culture),
            double number => FormatNumber(number, culture),
            float number => FormatNumber(number, culture),
            decimal number => FormatNumber((double)number, culture),
            int or long or short => Convert.ToString(value, culture) ?? string.Empty,
            string text => FormatText(text, culture),
            null => "?",
            _ => Convert.ToString(value, culture) ?? string.Empty,
        };
    }

    private static string FormatText(string? text, CultureInfo culture) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? FormatNumber(number, culture)
            : text ?? string.Empty;

    private static bool IsNumber(object? value) => value switch
    {
        JsonElement { ValueKind: JsonValueKind.Number } => true,
        JsonElement { ValueKind: JsonValueKind.String } element => IsNumericText(element.GetString()),
        string text => IsNumericText(text),
        double or float or decimal or int or long or short => true,
        _ => false,
    };

    private static bool IsNumericText(string? text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _);

    // Ten decimals hide binary rounding noise such as 0.30000000000000004.
    private static string FormatNumber(double number, CultureInfo culture) =>
        number.ToString("#,0.##########", culture);

    private static string? ReadString(object? value) =>
        value switch
        {
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            string text => text,
            _ => null,
        };

    private static bool IsError(object? value) =>
        ReadString(value) is { } text && text.StartsWith("Error:", StringComparison.OrdinalIgnoreCase);
}
