using System.Diagnostics.CodeAnalysis;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Reads and formats assistant text written as JSON, such as the playground's structured response.</summary>
public static class StructuredResponseFormatter
{
    private static readonly JsonSerializerOptions IndentedOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Gets whether text starts like a JSON object. Streaming text is recognized from its first character, before
    /// the object is complete.
    /// </summary>
    public static bool LooksLikeJsonObject(string? text)
    {
        if (text is null)
            return false;

        foreach (var character in text)
        {
            if (!char.IsWhiteSpace(character))
                return character == '{';
        }

        return false;
    }

    /// <summary>Formats complete JSON with indentation, or returns <see langword="null"/> while it is incomplete.</summary>
    public static string? TryFormat(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        try
        {
            var node = JsonNode.Parse(text);
            return node is null ? "null" : node.ToJsonString(IndentedOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads a complete JSON object that follows the <see cref="PlaygroundResponse"/> schema, which requires at least
    /// a summary.
    /// </summary>
    public static bool TryReadResponse(string? text, [NotNullWhen(true)] out PlaygroundResponse? response)
    {
        response = null;
        if (!LooksLikeJsonObject(text))
            return false;

        try
        {
            response = JsonSerializer.Deserialize(text!, PlaygroundJsonContext.Default.PlaygroundResponse);
        }
        catch (JsonException)
        {
            return false;
        }

        if (response is null || string.IsNullOrWhiteSpace(response.Summary))
        {
            response = null;
            return false;
        }

        response.KeyPoints = [.. (response.KeyPoints ?? []).Where(static point => !string.IsNullOrWhiteSpace(point))];
        return true;
    }
}
