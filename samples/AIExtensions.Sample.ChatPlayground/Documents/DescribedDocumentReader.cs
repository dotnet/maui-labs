using Microsoft.Extensions.DataIngestion;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Associates a document reader with its playground provider description.</summary>
public sealed class DescribedDocumentReader(IngestionDocumentReader inner, DocumentReaderDescriptor descriptor)
    : IngestionDocumentReader
{
    public DocumentReaderDescriptor Descriptor { get; } = descriptor;

    public override Task<IngestionDocument> ReadAsync(
        Stream source, string identifier, string mediaType, CancellationToken cancellationToken = default) =>
        inner.ReadAsync(source, identifier, mediaType, cancellationToken);
}
