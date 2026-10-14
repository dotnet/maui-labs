using System.Text.Json;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Finalizes, renders, and disposes extraction results before returning an independent display value.</summary>
public sealed class DocumentExtractionService
{
    private static readonly JsonSerializerOptions s_options = new() { WriteIndented = true };

    public async Task<DocumentExtractionRunResult> ExtractAsync(
        IDocumentExtractionClient client, SelectedDocument input, bool streamPages,
        IProgress<DocumentExtractionProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        using var source = new MemoryStream(input.Bytes, writable: false);
        using var result = streamPages
            ? await BuildStreamAsync(client, source, input, progress, cancellationToken).ConfigureAwait(false)
            : await client.ExtractAsync(source, input.FileName, input.MediaType, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var output = JsonSerializer.Serialize(new
        {
            Document = new
            {
                result.Document.Identifier,
                result.Document.StoredMarkdown,
                SemanticMarkdown = result.Document.ToMarkdown(),
                Sections = result.Document.Sections.Select(section => new
                {
                    Elements = section.Elements.Select(RenderElement).ToArray(),
                }).ToArray(),
            },
            Pages = result.Pages.Select(page => new { page.PageNumber, page.Markdown, ObservationCount = page.Observations.Count }),
        }, s_options);
        cancellationToken.ThrowIfCancellationRequested();
        return new DocumentExtractionRunResult(result.Pages.Count, result.Pages.Sum(page => page.Observations.Count), output);
    }

    private static async Task<DocumentExtractionResult> BuildStreamAsync(
        IDocumentExtractionClient client, Stream source, SelectedDocument input,
        IProgress<DocumentExtractionProgress>? progress, CancellationToken cancellationToken)
    {
        using var builder = new DocumentExtractionDocumentBuilder(input.FileName);
        var observed = 0;
        await foreach (var page in client.ExtractPagesAsync(source, input.FileName, input.MediaType, cancellationToken)
            .ConfigureAwait(false))
        {
            builder.AddPage(page);
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new DocumentExtractionProgress(++observed));
        }
        cancellationToken.ThrowIfCancellationRequested();
        return builder.Build();
    }

    private static object RenderElement(DocumentElement element) => element switch
    {
        DocumentTextElement text => new { Type = "text", text.Kind, text.Text, Markdown = text.GetMarkdown(), text.HeaderLevel },
        DocumentImageElement image => new
        {
            Type = "image", image.Text, Markdown = image.GetMarkdown(), image.MediaType,
            image.AlternativeText, ContentLength = image.Content?.Length,
        },
        DocumentTableElement table => new
        {
            Type = "table", table.Text, Markdown = table.GetMarkdown(), table.Cells.RowCount, table.Cells.ColumnCount,
            Cells = table.Cells.Where(cell => cell is not null).Select(cell => new
            {
                cell!.RowIndex, cell.ColumnIndex, Content = RenderElement(cell.Content),
            }).ToArray(),
        },
        DocumentSection section => new { Type = "section", Elements = section.Elements.Select(RenderElement).ToArray() },
        DocumentExtensionElement extension => new
        {
            Type = "extension", ExtensionType = extension.Type, extension.Text, Markdown = extension.GetMarkdown(),
        },
        _ => new { Type = "element", element.Text, Markdown = element.GetMarkdown() },
    };
}

public sealed record DocumentExtractionProgress(int PagesProcessed);

public sealed record DocumentExtractionRunResult(int PageCount, int ObservationCount, string Output);
