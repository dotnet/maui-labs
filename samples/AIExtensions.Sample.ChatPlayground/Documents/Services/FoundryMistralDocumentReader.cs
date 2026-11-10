using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.DocumentExtraction;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Projects Toolkit Mistral OCR output without independently interpreting the service's JSON or HTML.</summary>
internal sealed class FoundryMistralDocumentReader(DocumentExtractionClient client) : IngestionDocumentReader
{
    private readonly DocumentExtractionClient _client = client ?? throw new ArgumentNullException(nameof(client));

    public override async Task<IngestionDocument> ReadAsync(
        Stream source, string identifier, string mediaType, CancellationToken cancellationToken = default) =>
        CloudDocumentIngestionProjection.Project(
            await _client.ExtractAsync(source, identifier, mediaType, cancellationToken: cancellationToken)
                .ConfigureAwait(false), cancellationToken);
}
