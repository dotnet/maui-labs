using System.Text;
using Microsoft.Extensions.DataIngestion;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Runs a real reader and formats standard ingestion elements independently of MAUI controls.</summary>
public sealed class DocumentReadingService
{
    public async Task<DocumentReadResult> ReadAsync(
        IngestionDocumentReader reader, SelectedDocument input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        using var source = new MemoryStream(input.Bytes, writable: false);
        var document = await reader.ReadAsync(source, input.FileName, input.MediaType, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var output = new StringBuilder().AppendLine($"Document: {document.Identifier}");
        foreach (var section in document.Sections)
        {
            cancellationToken.ThrowIfCancellationRequested();
            output.AppendLine().AppendLine($"Page {section.PageNumber}");
            foreach (var element in section.Elements)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var type = element switch
                {
                    IngestionDocumentHeader => "Header",
                    IngestionDocumentTable => "Table",
                    IngestionDocumentParagraph => "Paragraph",
                    _ => "Element",
                };
                output.Append(type).Append(": ").AppendLine(element.Text ?? element.GetMarkdown());
                output.AppendLine("Markdown:").AppendLine(element.GetMarkdown()).AppendLine();
            }
        }
        return new DocumentReadResult(document.Sections.Count, output.ToString());
    }
}

public sealed record DocumentReadResult(int PageCount, string Output);
