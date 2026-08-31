namespace Microsoft.Maui.Cli.DevFlow.Execution;

internal static class FlowPlatformTags
{
    private static readonly char[] Separators = [',', ';', '|'];

    public static IReadOnlyList<string> Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];

        var trimmed = value.Trim().Trim('[', ']');
        var tags = trimmed
            .Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Normalize)
            .Where(static tag => tag.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return tags.Length > 0 ? tags : [Normalize(trimmed)];
    }

    public static bool Matches(string? value, IReadOnlyCollection<string> aliases)
    {
        ArgumentNullException.ThrowIfNull(aliases);
        var normalizedAliases = aliases
            .Select(Normalize)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Parse(value).Any(normalizedAliases.Contains);
    }

    public static IReadOnlyList<string> Parse(IEnumerable<string>? values)
    {
        // A plan may declare no platforms at all. That is a valid, unconstrained plan, not a
        // programming error, so it must not throw its way out as an infrastructure failure.
        if (values is null)
            return [];
        return values
            .Where(static value => value is not null)
            .SelectMany(Parse)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string Normalize(string value)
    {
        var normalized = value.Trim().Trim('"', '\'').ToLowerInvariant();
        while (normalized.Contains("  ", StringComparison.Ordinal))
            normalized = normalized.Replace("  ", " ", StringComparison.Ordinal);
        return normalized.Replace('_', '-').Replace(' ', '-');
    }
}
