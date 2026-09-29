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
        $"Foundry model ({deploymentName})",
        "Cloud semantic extraction through a deployed vision-capable model. PDFs and images are sent to the model. Text and tables are structured, but provider-grade polygons and confidence are unavailable.",
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
        Deployed Microsoft Foundry model

        Deployment: {deploymentName}
        Input: PDF, PNG, or JPEG through the Responses API
        Output: structured page text, semantic blocks, and tables
        Geometry: unavailable

        The model receives the full document. For PDF input, vision-capable Responses models receive both extracted text and page images. Results are probabilistic and may vary between requests.
        """;

    public void Dispose() => chatClient.Dispose();
}
