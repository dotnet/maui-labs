using Microsoft.Extensions.AI;
using Microsoft.Extensions.DocumentExtraction;

namespace AIExtensions.Sample.ChatPlayground;

internal sealed class FoundryModelDocumentExtractionProvider(
    IChatClient chatClient,
    string deploymentName)
    : IDocumentExtractionProvider, IDisposable
{
    public DocumentProviderDescriptor Descriptor { get; } = new(
        "foundry-model",
        $"Vision chat model ({deploymentName})",
        "Uses the app's existing vision-capable chat deployment for general semantic document extraction. Provider-grade polygons and confidence are unavailable.",
        IsAvailable: true,
        SendsDocumentOffDevice: true);

    public IDocumentExtractionClient CreateClient(string mediaType) =>
        new FoundryModelDocumentExtractionClient(
            chatClient,
            deploymentName);

    public DocumentExtractionOptions CreateOptions(DocumentExtractionSettings settings) =>
        new()
        {
            ModelId = deploymentName,
        };

    public string GetCapabilitiesSummary() =>
        $"""
        Vision-capable chat deployment

        Deployment: {deploymentName}
        Input: PDF, PNG, or JPEG through the Responses API
        Output: structured page text, semantic blocks, and tables
        Geometry: unavailable

        The model receives the full document. For PDF input, vision-capable Responses models receive both extracted text and page images. Results are probabilistic and may vary between requests. This client is selected explicitly and never invoked as an automatic fallback for another provider.
        """;

    public void Dispose() => chatClient.Dispose();
}
