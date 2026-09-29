using Microsoft.Extensions.DocumentExtraction;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>An image or PDF selected for document extraction.</summary>
public sealed record DocumentInput(
    string FileName,
    string MediaType,
    byte[] Data,
    IReadOnlyList<DocumentPreviewPage>? ProvidedPreviewPages = null)
{
    public bool IsImage => MediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

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

internal static class DocumentMediaTypes
{
    internal static string? FromFileName(string fileName)
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
}

/// <summary>Describes one registered document-extraction client.</summary>
public sealed record DocumentExtractionClientDescriptor(
    string Id,
    string Name,
    string Description);

public sealed record DocumentClientOption(
    IDocumentExtractionClient Client,
    DocumentExtractionClientDescriptor Descriptor,
    int Index)
{
    public string AutomationId => $"DocumentClient{Index}Radio";

    public override string ToString() => Descriptor.Name;
}
