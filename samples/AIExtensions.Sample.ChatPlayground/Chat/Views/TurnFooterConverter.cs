using System.Globalization;
using Microsoft.Extensions.AI;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>
/// Describes a turn under its response, from its <c>ModelId</c>, <c>Usage</c> and <c>IsStopped</c>: whether it was
/// stopped, the model that answered and the tokens it used, such as <c>gpt-4.1 · 1,024 in · 128 out</c>.
/// </summary>
/// <remarks>
/// It is a multi-value converter, so the footer binds to each property and updates when the turn reports it; a binding
/// to the turn itself would keep its first value.
/// </remarks>
public sealed class TurnFooterConverter : IMultiValueConverter
{
    /// <inheritdoc />
    public object? Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var modelId = values.Length > 0 ? values[0] as string : null;
        var usage = values.Length > 1 ? values[1] as UsageDetails : null;
        var stopped = values.Length > 2 && values[2] is true;
        var text = Format(modelId, usage, culture);
        if (stopped)
            text = text.Length > 0 ? $"Stopped \u00B7 {text}" : "Stopped";

        return text;
    }

    /// <inheritdoc />
    public object?[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    /// <summary>Formats a model id and token usage, leaving out whatever the provider did not report.</summary>
    public static string Format(string? modelId, UsageDetails? usage, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        var parts = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(modelId))
            parts.Add(modelId);

        if (usage is { InputTokenCount: { } input })
            parts.Add($"{input.ToString("N0", culture)} in");
        if (usage is { OutputTokenCount: { } output })
            parts.Add($"{output.ToString("N0", culture)} out");
        if (usage is { InputTokenCount: null, OutputTokenCount: null, TotalTokenCount: { } total })
            parts.Add($"{total.ToString("N0", culture)} tokens");

        return string.Join(" \u00B7 ", parts);
    }
}
