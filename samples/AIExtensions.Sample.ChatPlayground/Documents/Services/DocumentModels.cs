using Microsoft.Extensions.DocumentExtraction;
using ExtractedDocumentPage = Microsoft.Extensions.DocumentExtraction.DocumentPage;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>An image or PDF selected for document extraction.</summary>
public sealed record DocumentInput(
    string FileName,
    string MediaType,
    byte[] Data,
    IReadOnlyList<DocumentPreviewPage>? ProvidedPreviewPages = null)
{
    public bool IsImage => MediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

    public bool IsPdf => string.Equals(MediaType, "application/pdf", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<DocumentPreviewPage> PreviewPages =>
        ProvidedPreviewPages ??
        (IsImage ? [new DocumentPreviewPage(1, "Page 1", Data)] : []);

    public string Details => $"{MediaType} - {FormatBytes(Data.LongLength)}";

    public Stream OpenRead() => new MemoryStream(Data, writable: false);

    private static string FormatBytes(long bytes) =>
        bytes switch
        {
            >= 1024 * 1024 => $"{bytes / (1024d * 1024d):F1} MB",
            >= 1024 => $"{bytes / 1024d:F1} KB",
            _ => $"{bytes} bytes",
        };
}

/// <summary>One image used to preview a selected document page.</summary>
public sealed record DocumentPreviewPage(
    int PageNumber,
    string Label,
    byte[] Data);

/// <summary>Provider-specific settings exposed by the Documents playground.</summary>
public sealed record DocumentExtractionSettings(
    bool DetectBarcodes,
    bool AutomaticallyDetectLanguage);

/// <summary>Reports one completed page while a document is being extracted.</summary>
public readonly record struct DocumentExtractionProgress(
    int? PagesProcessed,
    int? TotalPages,
    ExtractedDocumentPage Page);

/// <summary>Describes the document provider available to the playground.</summary>
public sealed record DocumentProviderDescriptor(
    string DisplayName,
    string Description,
    bool IsAvailable);

public interface IDocumentExtractionProvider
{
    DocumentProviderDescriptor Descriptor { get; }

    IDocumentExtractionClient CreateClient(string mediaType);

    DocumentExtractionOptions CreateOptions(DocumentExtractionSettings settings);

    string GetCapabilitiesSummary();
}
