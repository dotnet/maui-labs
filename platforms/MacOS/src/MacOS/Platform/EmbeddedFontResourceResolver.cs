namespace Microsoft.Maui.Platforms.MacOS.Platform;

internal static class EmbeddedFontResourceResolver
{
    public static string? FindResource(string filename, IReadOnlyList<string> resourceNames)
    {
        var normalized = filename.Replace('\\', '/');
        var exactName = normalized.Replace('/', '.');
        var exact = Matches(exactName);
        if (exact.Length > 0)
            return Unique(exact);

        return Unique(Matches(Path.GetFileName(normalized)));

        string[] Matches(string name) => resourceNames.Where(resource =>
            resource.Equals(name, StringComparison.OrdinalIgnoreCase) ||
            resource.EndsWith("." + name, StringComparison.OrdinalIgnoreCase)).ToArray();

        string? Unique(string[] matches) => matches.Length switch
        {
            0 => null,
            1 => matches[0],
            _ => throw new InvalidOperationException(
                $"Embedded font '{filename}' is ambiguous: {string.Join(", ", matches)}.")
        };
    }
}
