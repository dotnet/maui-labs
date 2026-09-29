using Microsoft.Extensions.DocumentExtraction;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Exposes playground metadata through the document client's service-discovery API.</summary>
internal sealed class DescribedDocumentExtractionClient(
    IDocumentExtractionClient innerClient,
    DocumentExtractionClientDescriptor descriptor)
    : DelegatingDocumentExtractionClient(innerClient)
{
    public override object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType == typeof(DocumentExtractionClientDescriptor) && serviceKey is null
            ? descriptor
            : base.GetService(serviceType, serviceKey);
}
