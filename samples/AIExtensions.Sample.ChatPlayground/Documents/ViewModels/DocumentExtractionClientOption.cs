using Microsoft.Extensions.DocumentExtraction;

namespace AIExtensions.Sample.ChatPlayground;

public sealed record DocumentExtractionClientOption(DocumentExtractionClient Client, int Index)
{
    public DocumentExtractionClientDescriptor Descriptor { get; } =
        Client.GetService(typeof(DocumentExtractionClientDescriptor)) as DocumentExtractionClientDescriptor
        ?? throw new InvalidOperationException($"Document extraction client {Index} did not expose its descriptor.");

    public string AutomationId => $"DocumentExtractionClient{Index}Radio";
}
