using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using Azure.AI.DocumentIntelligence;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;

namespace AIExtensions.Sample.ChatPlayground;

internal sealed class AzureDocumentIntelligenceExtractionClient : IDocumentExtractionClient
{
    private readonly DocumentIntelligenceClient _client;
    private readonly string _modelId;
    private bool _disposed;

    public AzureDocumentIntelligenceExtractionClient(Uri endpoint, string key, string modelId = "prebuilt-layout")
        : this(AzureDocumentIntelligenceReader.CreateClient(endpoint, key), modelId)
    {
    }

    internal AzureDocumentIntelligenceExtractionClient(DocumentIntelligenceClient client, string modelId = "prebuilt-layout")
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        _modelId = modelId;
    }

    public async IAsyncEnumerable<DocumentExtractionPage> ExtractPagesAsync(
        Stream document, string identifier, string mediaType,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        var result = await AzureDocumentIntelligenceReader.AnalyzeAsync(
            _client, document, mediaType, _modelId, cancellationToken).ConfigureAwait(false);
        foreach (var page in MapPages(result, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return page;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (serviceKey is not null)
            return null;
        return serviceType.IsInstanceOfType(this) ? this : serviceType.IsInstanceOfType(_client) ? _client : null;
    }

    public void Dispose() => _disposed = true;

    internal static IReadOnlyList<DocumentExtractionPage> MapPages(AnalyzeResult result, CancellationToken token = default)
    {
        var elements = new List<(int Page, int Offset, int Sequence, DocumentExtractionObservation Observation)>();
        var tables = result.Tables ?? [];
        var sequence = 0;
        foreach (var paragraph in result.Paragraphs ?? [])
        {
            token.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(paragraph.Content))
                continue;
            var number = AzureDocumentIntelligenceReader.GetPage(paragraph.BoundingRegions, paragraph.Spans, result.Pages);
            if (paragraph.Spans is { Count: > 0 } && tables.Any(table =>
                AzureDocumentIntelligenceReader.GetTablePage(table, result.Pages) == number &&
                paragraph.Spans.All(span => table.Spans.Any(tableSpan =>
                    span.Offset >= tableSpan.Offset &&
                    (long)span.Offset + span.Length <= (long)tableSpan.Offset + tableSpan.Length))))
                continue;
            var order = sequence++;
            var text = paragraph.Content ?? "";
            elements.Add((number, AzureDocumentIntelligenceReader.GetOffset(paragraph.Spans), order,
                new DocumentExtractionText($"page-{number}/paragraph-{order}", order,
                    paragraph.Role == ParagraphRole.Title || paragraph.Role == ParagraphRole.SectionHeading
                        ? DocumentTextKind.Header : DocumentTextKind.Paragraph, text, text)));
        }
        foreach (var table in tables)
        {
            token.ThrowIfCancellationRequested();
            AzureDocumentIntelligenceReader.ValidateTable(table, token);
            var number = AzureDocumentIntelligenceReader.GetTablePage(table, result.Pages);
            var order = sequence++;
            var id = $"page-{number}/table-{order}";
            var cells = table.Cells.Where(cell => !string.IsNullOrEmpty(cell.Content))
                .Select((cell, index) => new DocumentExtractionTableCell(cell.RowIndex, cell.ColumnIndex,
                new DocumentExtractionText($"{id}/cell-{index}", index, DocumentTextKind.Paragraph,
                    cell.Content ?? "", cell.Content ?? ""))).ToArray();
            var grid = new string?[table.RowCount, table.ColumnCount];
            foreach (var cell in cells)
                grid[cell.Row, cell.Column] = cell.Content.Markdown;
            var display = new StringBuilder("<table>");
            for (var row = 0; row < table.RowCount; row++)
            {
                token.ThrowIfCancellationRequested();
                display.Append("<tr>");
                for (var column = 0; column < table.ColumnCount; column++)
                    display.Append("<td>").Append(WebUtility.HtmlEncode(grid[row, column] ?? "")).Append("</td>");
                display.Append("</tr>");
            }
            display.Append("</table>");
            elements.Add((number, AzureDocumentIntelligenceReader.GetOffset(table.Spans), order,
                new DocumentExtractionTable(id, order, display.ToString(), table.RowCount, table.ColumnCount, cells)));
        }

        var pages = new List<DocumentExtractionPage>();
        foreach (var page in result.Pages.OrderBy(page => page.PageNumber))
        {
            token.ThrowIfCancellationRequested();
            var observations = elements.Where(item => item.Page == page.PageNumber)
                .OrderBy(item => item.Offset).ThenBy(item => item.Sequence)
                .Select((item, order) => Reorder(item.Observation, order)).ToList();
            if (observations.Count == 0)
                foreach (var line in page.Lines ?? [])
                {
                    token.ThrowIfCancellationRequested();
                    var text = line.Content ?? "";
                    if (text.Length == 0)
                        continue;
                    observations.Add(new DocumentExtractionText($"page-{page.PageNumber}/line-{observations.Count}",
                        observations.Count, DocumentTextKind.Paragraph, text, text));
                }
            var markdown = result.ContentFormat == DocumentContentFormat.Markdown && page.Spans.Count > 0
                ? string.Join("", page.Spans.Select(span => Slice(result.Content, span))) : null;
            pages.Add(new DocumentExtractionPage(page.PageNumber, observations, markdown));
        }
        return pages;
    }

    private static DocumentExtractionObservation Reorder(DocumentExtractionObservation observation, int order) => observation switch
    {
        DocumentExtractionText text => new DocumentExtractionText(text.ProviderId, order, text.Kind, text.Markdown,
            text.Text, text.HeaderLevel),
        DocumentExtractionTable table => new DocumentExtractionTable(table.ProviderId, order, table.Markdown,
            table.RowCount, table.ColumnCount, table.Cells),
        _ => throw new InvalidOperationException("Unsupported Azure observation."),
    };

    private static string Slice(string content, DocumentSpan span)
    {
        if (span.Offset < 0 || span.Length < 0 || (long)span.Offset + span.Length > content.Length)
            throw new InvalidDataException("Cloud returned an invalid document span.");
        return content.Substring(span.Offset, span.Length);
    }
}
