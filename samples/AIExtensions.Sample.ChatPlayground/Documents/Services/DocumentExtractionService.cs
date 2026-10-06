using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DocumentExtraction;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Runs the proposed client contract independently of MAUI controls.</summary>
public sealed class DocumentExtractionService
{
    private static readonly JsonSerializerOptions s_normalizedOptions = new()
    {
        WriteIndented = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers =
            {
                type =>
                {
                    if (type.Type == typeof(DocumentExtractionResult))
                        foreach (var property in type.Properties)
                            if (property.Name == nameof(DocumentExtractionResult.RawRepresentation))
                                property.ShouldSerialize = (_, _) => false;
                },
            },
        },
    };

    public async Task<DocumentExtractionRunResult> ExtractAsync(
        IDocumentExtractionClient client,
        SelectedDocument input,
        bool streamPages,
        DocumentExtractionOptions? options = null,
        IProgress<DocumentExtractionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        using var source = new MemoryStream(input.Bytes, writable: false);
        DocumentExtractionResult result;
        var rawUpdates = new List<object>();
        var seenRaw = new HashSet<object>(ReferenceEqualityComparer.Instance);
        if (streamPages)
        {
            var updates = new List<DocumentExtractionPageResult>();
            await foreach (var update in client.ExtractPagesAsync(source, input.MediaType, options, cancellationToken)
                .ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                updates.Add(update);
                if (update.RawRepresentation is { } rawUpdate && seenRaw.Add(rawUpdate))
                    rawUpdates.Add(rawUpdate);
                progress?.Report(new DocumentExtractionProgress(update.PagesProcessed, update.TotalPages));
            }
            result = updates.ToDocumentExtractionResult();
        }
        else
        {
            result = await client.ExtractAsync(source, input.MediaType, options, cancellationToken).ConfigureAwait(false);
        }
        cancellationToken.ThrowIfCancellationRequested();
        var serialization = new JsonSerializerOptions { WriteIndented = true };
        var output = JsonSerializer.Serialize(result, s_normalizedOptions);
        var rawPages = result.Pages.Select(page => page.RawRepresentation).ToArray();
        object? rawResult = result.RawRepresentation;
        if (rawResult is null && rawUpdates.Count > 0)
            rawResult = rawUpdates.Count == 1 ? rawUpdates[0] : rawUpdates;
        if (rawResult is null && rawPages.Any(page => page is not null))
            rawResult = rawPages;
        var raw = rawResult is null ? string.Empty : JsonSerializer.Serialize(rawResult, serialization);
        return new DocumentExtractionRunResult(result, output, raw);
    }
}

public sealed record DocumentExtractionProgress(int? PagesProcessed, int? TotalPages);

public sealed record DocumentExtractionRunResult(DocumentExtractionResult Result, string Output, string RawOutput);
