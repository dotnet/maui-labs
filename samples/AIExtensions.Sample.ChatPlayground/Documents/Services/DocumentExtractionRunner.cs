using Microsoft.Extensions.DocumentExtraction;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Runs one selected document through a selected document provider.</summary>
public sealed class DocumentExtractionRunner
{
    public static string? GetMediaType(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".heic" => "image/heic",
            ".tif" or ".tiff" => "image/tiff",
            ".pdf" => "application/pdf",
            _ => null,
        };
    }

    public async Task<DocumentExtractionResult> ExtractAsync(
        DocumentInput input,
        IDocumentExtractionProvider provider,
        DocumentExtractionSettings settings,
        IProgress<DocumentExtractionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(settings);

        if (!provider.Descriptor.IsAvailable)
            throw new NotSupportedException(provider.Descriptor.Description);

        using var client = provider.CreateClient(input.MediaType);
        using var stream = input.OpenRead();
        var pages = new List<DocumentExtractionPageResult>();
        var options = provider.CreateOptions(settings);

        await foreach (var page in client
            .ExtractPagesAsync(stream, input.MediaType, options, cancellationToken)
            .WithCancellation(cancellationToken)
            .ConfigureAwait(false))
        {
            pages.Add(page);
            progress?.Report(new DocumentExtractionProgress(
                page.PagesProcessed,
                page.TotalPages,
                page.Page));
        }

        return pages.ToDocumentExtractionResult();
    }

    public string GetCapabilitiesSummary(IDocumentExtractionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return provider.GetCapabilitiesSummary();
    }
}
