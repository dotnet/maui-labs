using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.DocumentExtraction;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Projects the same Toolkit extraction used by the extraction playground into ingestion elements.</summary>
internal sealed class AzureDocumentIntelligenceReader(DocumentExtractionClient client) : IngestionDocumentReader
{
    private readonly DocumentExtractionClient _client = client ?? throw new ArgumentNullException(nameof(client));

    public override async Task<IngestionDocument> ReadAsync(
        Stream source, string identifier, string mediaType, CancellationToken cancellationToken = default) =>
        CloudDocumentIngestionProjection.Project(
            await _client.ExtractAsync(source, identifier, mediaType, cancellationToken: cancellationToken)
                .ConfigureAwait(false), cancellationToken);
}
