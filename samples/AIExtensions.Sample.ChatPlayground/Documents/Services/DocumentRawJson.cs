using System.Text.Json;
using Microsoft.Extensions.DocumentExtraction;

namespace AIExtensions.Sample.ChatPlayground;

internal static class DocumentRawJson
{
    private static readonly JsonSerializerOptions s_indented = new() { WriteIndented = true };

    internal static Func<string>? CreateFactory(object? rawRepresentation) =>
        rawRepresentation is JsonElement json ? () => Format(json) : null;

    internal static string SerializeNormalized(DocumentExtractionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return JsonSerializer.Serialize(result, s_indented);
    }

    internal static string SerializePages(DocumentExtractionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var firstRaw = result.Pages.Select(static page => page.RawRepresentation).OfType<JsonElement>().FirstOrDefault();
        if (firstRaw.ValueKind == JsonValueKind.Object &&
            firstRaw.TryGetProperty("pages", out var providerPages) &&
            providerPages.ValueKind == JsonValueKind.Array)
        {
            return Format(firstRaw);
        }

        var pages = result.Pages.Select(static page =>
        {
            return new
            {
                page.PageNumber,
                Raw = page.RawRepresentation is JsonElement raw ? raw : (JsonElement?)null,
            };
        });
        return JsonSerializer.Serialize(pages, s_indented);
    }

    private static string Format(JsonElement json) => JsonSerializer.Serialize(json, s_indented);
}
