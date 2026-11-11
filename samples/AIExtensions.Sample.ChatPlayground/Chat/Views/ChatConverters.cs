using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>
/// Formats the arguments of a tool call one per line, such as <c>left: 12</c>, or only the value of the argument that
/// the converter parameter names.
/// </summary>
public sealed class ToolArgumentsConverter : IValueConverter
{
    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not FunctionCallContent { Arguments: { Count: > 0 } arguments })
            return null;

        if (parameter is string name)
            return arguments.TryGetValue(name, out var argument) ? ToolValues.Format(argument) : null;

        return string.Join('\n', arguments.Select(argument => $"{argument.Key}: {ToolValues.Format(argument.Value)}"));
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Formats a tool call's result: the error of a call that failed, or what it returned.</summary>
public sealed class ToolResultConverter : IValueConverter
{
    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is FunctionResultContent result
            ? result.Exception?.Message ?? ToolValues.Format(result.Result)
            : null;

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Describes an approval once it no longer waits for the user.</summary>
public sealed class ApprovalStatusConverter : IValueConverter
{
    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        ApprovalStatus.Approved => "Approved",
        ApprovalStatus.Rejected => "Rejected",

        // Pending, but no longer waiting: the turn stopped or failed, or the conversation was restored.
        _ => "Not answered",
    };

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Says why a turn failed, from its <see cref="ConversationTurn.Error"/>.</summary>
public sealed class TurnErrorConverter : IValueConverter
{
    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Exception error ? $"The response failed: {DisplayText.OneLine(error.Message)}" : null;

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Converts the content of a <see cref="MediaContentBlock"/> to an image, when it is one.</summary>
public sealed class MediaImageConverter : IValueConverter
{
    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        DataContent { Data.Length: > 0 } data when data.HasTopLevelMediaType("image") =>
            ImageSource.FromStream(() => new MemoryStream(data.Data.ToArray())),
        UriContent uri when uri.HasTopLevelMediaType("image") => ImageSource.FromUri(uri.Uri),
        _ => null,
    };

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Converts a value to whether there is anything to show: not null, not blank text, not an empty list.</summary>
public sealed class HasValueConverter : IValueConverter
{
    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        null => false,
        string text => !string.IsNullOrWhiteSpace(text),
        System.Collections.ICollection collection => collection.Count > 0,
        _ => true,
    };

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Shows text with each <c>**phrase**</c> within a line in bold, such as the headings of a reasoning summary, and
/// everything else as it is.
/// </summary>
public sealed class BoldTextConverter : IValueConverter
{
    private const string Marker = "**";

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string text)
            return null;

        var formatted = new FormattedString();
        var plainStart = 0;
        var open = text.IndexOf(Marker, StringComparison.Ordinal);
        while (open >= 0)
        {
            var start = open + Marker.Length;
            var close = text.IndexOf(Marker, start, StringComparison.Ordinal);
            if (close < 0)
                break;

            // A phrase stays on its line and has no space just inside its markers, so 2 ** 3 stays as it is.
            var lineEnd = text.IndexOf('\n', start);
            var isPhrase = close > start && (lineEnd < 0 || close < lineEnd) &&
                !char.IsWhiteSpace(text[start]) && !char.IsWhiteSpace(text[close - 1]);
            if (!isPhrase)
            {
                open = close;
                continue;
            }

            Add(formatted, text[plainStart..open], bold: false);
            Add(formatted, text[start..close], bold: true);
            plainStart = close + Marker.Length;
            open = text.IndexOf(Marker, plainStart, StringComparison.Ordinal);
        }

        Add(formatted, text[plainStart..], bold: false);
        return formatted;
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    // Plain text leaves its font unset, so it keeps the label's.
    private static void Add(FormattedString formatted, string text, bool bold)
    {
        if (text.Length == 0)
            return;
        formatted.Spans.Add(bold ? new Span { Text = text, FontAttributes = FontAttributes.Bold } : new Span { Text = text });
    }
}

/// <summary>Formats text for the chat's status lines and footers.</summary>
internal static class DisplayText
{
    /// <summary>Joins text that spans several lines, such as a provider error with its details, into one.</summary>
    public static string OneLine(string text) =>
        string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}

/// <summary>Formats the values that tool calls carry, which arrive as JSON from a provider or as .NET values.</summary>
internal static class ToolValues
{
    /// <summary>Formats a value as text: a JSON string as its text, other JSON as JSON.</summary>
    public static string Format(object? value) => value switch
    {
        null => string.Empty,
        string text => text,
        JsonElement { ValueKind: JsonValueKind.String } element => element.GetString() ?? string.Empty,
        JsonElement element => element.GetRawText(),
        IFormattable formattable => formattable.ToString(null, CultureInfo.CurrentCulture),
        _ => value.ToString() ?? string.Empty,
    };
}
