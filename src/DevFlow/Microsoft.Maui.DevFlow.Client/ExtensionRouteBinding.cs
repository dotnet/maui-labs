using System.Text.Json;

namespace Microsoft.Maui.DevFlow;

internal static class ExtensionRouteBinding
{
    internal static string Bind(string path, JsonElement? parameters, out Dictionary<string, string> routeValues)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var bound = string.Join("/", path.Split('/').Select(segment =>
        {
            if (!segment.StartsWith("{", StringComparison.Ordinal) || !segment.EndsWith("}", StringComparison.Ordinal))
                return segment;
            var name = segment.Substring(1, segment.Length - 2);
            if (parameters is not { ValueKind: JsonValueKind.Object } args ||
                !args.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
                throw new ArgumentException($"Route parameter '{name}' must be supplied as a string.");
            var text = value.GetString()!;
            if (text is "." or "..")
                throw new ArgumentException($"Route parameter '{name}' cannot be a dot segment.");
            values[name] = text;
            return Uri.EscapeDataString(text);
        }));
        routeValues = values;
        return bound;
    }
}
