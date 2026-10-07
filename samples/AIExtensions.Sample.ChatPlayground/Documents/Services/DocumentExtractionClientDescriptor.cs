using Microsoft.Extensions.DocumentExtraction;

namespace AIExtensions.Sample.ChatPlayground;

public sealed record DocumentExtractionClientDescriptor(string Id, string DisplayName, string Description, bool IsCloud = false);

internal sealed class DescribedDocumentExtractionClient(
    IDocumentExtractionClient client, DocumentExtractionClientDescriptor descriptor) : DelegatingDocumentExtractionClient(client)
{
    public override object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType == typeof(DocumentExtractionClientDescriptor)
            ? descriptor
            : base.GetService(serviceType, serviceKey);
}

public sealed record DocumentExtractionClientOption(IDocumentExtractionClient Client, int Index)
{
    public DocumentExtractionClientDescriptor Descriptor { get; } =
        Client.GetService<DocumentExtractionClientDescriptor>()
        ?? throw new InvalidOperationException($"Document extraction client {Index} did not expose its descriptor.");

    public string AutomationId => $"DocumentExtractionClient{Index}Radio";
}
