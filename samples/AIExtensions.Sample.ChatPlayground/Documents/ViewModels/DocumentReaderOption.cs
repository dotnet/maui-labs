using Microsoft.Extensions.DataIngestion;

namespace AIExtensions.Sample.ChatPlayground;

public sealed record DocumentReaderOption(IngestionDocumentReader Reader, int Index)
{
    public DocumentReaderDescriptor Descriptor { get; } =
        Reader is DescribedDocumentReader described
            ? described.Descriptor
            : throw new InvalidOperationException($"Document reader {Index} did not expose its descriptor.");

    public string AutomationId => $"DocumentReader{Index}Radio";
}
