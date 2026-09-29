using Microsoft.Extensions.AI;
using Microsoft.Extensions.DocumentExtraction;

namespace AIExtensions.Sample.ChatPlayground;

internal sealed class FoundryMistralOcrProvider(
    HttpClient httpClient,
    Uri endpoint,
    string apiKey,
    string modelId)
    : IDocumentExtractionProvider, IDisposable
{
    public DocumentProviderDescriptor Descriptor { get; } = new(
        "foundry-mistral-ocr",
        $"Mistral OCR ({modelId})",
        "Specialized document-native OCR deployed in Microsoft Foundry. Returns per-page Markdown, classified blocks, pixel bounding boxes, tables, figures, and confidence. Document bytes leave this device.",
        IsAvailable: true,
        SendsDocumentOffDevice: true);

    public IDocumentExtractionClient CreateClient(string mediaType) =>
        new FoundryMistralOcrClient(
            httpClient,
            endpoint,
            apiKey,
            modelId);

    public DocumentExtractionOptions CreateOptions(DocumentExtractionSettings settings) =>
        new()
        {
            ModelId = modelId,
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                ["mistral.includeImages"] = settings.IncludeImages,
            },
        };

    public string GetCapabilitiesSummary() =>
        $"""
        Mistral OCR in Microsoft Foundry

        Model: {modelId}
        Route: /providers/mistral/azure/ocr
        Input: images or PDFs (Foundry catalog limit: 30 pages / 30 MB)
        Output: per-page Markdown, tables, figures, hyperlinks, headers/footers, classified blocks, bounding boxes, and confidence

        OCR 4 adds paragraph-level boxes and block types including title, header, footer, code, table, equation, paragraph, list, signature, image, caption, and references. Document bytes are sent to your Foundry resource and requests may incur charges.
        """;

    public void Dispose() => httpClient.Dispose();
}
