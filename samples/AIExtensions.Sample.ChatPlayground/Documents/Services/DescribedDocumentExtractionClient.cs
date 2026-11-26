using Microsoft.Extensions.DocumentExtraction;

namespace AIExtensions.Sample.ChatPlayground;

internal sealed class DescribedDocumentExtractionClient(
    DocumentExtractionClient client, DocumentExtractionClientDescriptor descriptor) : DelegatingDocumentExtractionClient(client)
{
    public override object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType == typeof(DocumentExtractionClientDescriptor)
            ? descriptor
            : base.GetService(serviceType, serviceKey);
}
