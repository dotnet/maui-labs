using Microsoft.Extensions.DocumentExtraction;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Runs the selected extraction client against a file.</summary>
public sealed class DocumentExtractionService
{
    public async Task<DocumentExtractionRunResult> ExtractAsync(
        DocumentExtractionClient client, InputFile input, DocumentExtractionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(input);
        using var source = new MemoryStream(input.Bytes, writable: false);
        var document = await client.ExtractAsync(source, input.FileName, input.MediaType, options, cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return new(document, DocumentResultFormatter.Format(document));
    }
}
