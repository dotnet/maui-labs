using System.Text.Json;
using Microsoft.Extensions.DocumentExtraction;

#if IOS || MACCATALYST
using Microsoft.Maui.Essentials.AI;
#endif

namespace AIExtensions.Sample.ChatPlayground;

internal static class DocumentRawJson
{
    internal static Func<string>? CreateFactory(object? rawRepresentation)
    {
#if IOS || MACCATALYST
        return rawRepresentation switch
        {
            FoundryModelDocumentRawReference foundry => () => Format(foundry.Json),
            FoundryMistralOcrRawReference mistral => () => Format(mistral.Json),
            AppleVisionDocumentNodeReference vision => () => Format(vision.GetRawJsonText()),
            ApplePdfKitPageReference
            {
                InnerRawRepresentation: AppleVisionDocumentNodeReference vision
            } => () => Format(vision.GetRawJsonText()),
            _ => null,
        };
#else
        return null;
#endif
    }

    internal static string SerializeNormalized(DocumentExtractionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

#if IOS || MACCATALYST
        var options = AppleDocumentExtractionJson.CreateOptions();
#else
        var options = new JsonSerializerOptions();
#endif
        options.WriteIndented = true;
        return JsonSerializer.Serialize(result, options);
    }

    internal static string SerializePages(DocumentExtractionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.Pages
            .Select(static page => page.RawRepresentation)
            .OfType<FoundryModelDocumentRawReference>()
            .FirstOrDefault() is { } foundry)
        {
            return Format(foundry.Json);
        }
        if (result.Pages
            .Select(static page => page.RawRepresentation)
            .OfType<FoundryMistralOcrRawReference>()
            .FirstOrDefault() is { } mistral)
        {
            return Format(mistral.Json);
        }

        var pages = result.Pages.Select(page =>
        {
            var factory = CreateFactory(page.RawRepresentation);
            return new
            {
                page.PageNumber,
                Raw = factory is null
                    ? (JsonElement?)null
                    : Parse(factory()),
            };
        });
        return JsonSerializer.Serialize(pages, new JsonSerializerOptions { WriteIndented = true });
    }

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static string Format(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(
                document.RootElement,
                new JsonSerializerOptions { WriteIndented = true });
        }
        catch (JsonException)
        {
            return json;
        }
    }
}
