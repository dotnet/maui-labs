using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.DocumentExtraction;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Projects the selected extraction client's result into ingestion elements without owning the client.</summary>
internal sealed class ExtractedDocumentReader(DocumentExtractionClient client) : IngestionDocumentReader
{
    private readonly DocumentExtractionClient _client = client ?? throw new ArgumentNullException(nameof(client));

    public override async Task<IngestionDocument> ReadAsync(
        Stream source, string identifier, string mediaType, CancellationToken cancellationToken = default) =>
        ExtractionDocumentIngestionMapper.Project(
            await _client.ExtractAsync(source, identifier, mediaType, cancellationToken: cancellationToken)
                .ConfigureAwait(false), cancellationToken);
}
