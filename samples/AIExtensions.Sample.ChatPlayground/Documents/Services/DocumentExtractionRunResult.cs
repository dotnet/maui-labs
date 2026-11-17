using Microsoft.Extensions.Documents;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Keeps the extracted document available for native inspection.</summary>
public sealed class DocumentExtractionRunResult(ExtractionDocument document, string output)
{
    public int PageCount => document.SourcePages.Count;
    public string Output { get; } = output;
    public bool HasRawOutput => document.RawRepresentation is not null || document.SourcePages.Count > 0;

    public Task<string> InspectRawAsync(CancellationToken cancellationToken = default) =>
        DocumentResultFormatter.FormatNativeAsync(document, cancellationToken);
}
