using System.Globalization;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>
/// Describes a completed turn for the response footer: the model that answered and the tokens it used, such as
/// <c>gpt-4.1 · 1,024 in · 128 out</c>. With the parameter <c>visible</c> it returns whether there is anything to show.
/// </summary>
public sealed class TurnFooterConverter : IValueConverter
{
    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = value is ConversationTurn turn ? Format(turn.ModelId, turn.Usage, culture) : string.Empty;
        return string.Equals(parameter as string, "visible", StringComparison.OrdinalIgnoreCase)
            ? text.Length > 0
            : text;
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
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
