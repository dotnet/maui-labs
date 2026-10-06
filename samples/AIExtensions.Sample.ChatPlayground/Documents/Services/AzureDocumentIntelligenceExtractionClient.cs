using System.Runtime.CompilerServices;
using Azure.AI.DocumentIntelligence;
using Microsoft.Extensions.DocumentExtraction;
using ExtractionPage = Microsoft.Extensions.DocumentExtraction.DocumentPage;
using ExtractionTable = Microsoft.Extensions.DocumentExtraction.DocumentTable;
using ExtractionCell = Microsoft.Extensions.DocumentExtraction.DocumentTableCell;
using ExtractionCellKind = Microsoft.Extensions.DocumentExtraction.DocumentTableCellKind;
using AzureCellKind = Azure.AI.DocumentIntelligence.DocumentTableCellKind;

namespace AIExtensions.Sample.ChatPlayground;

internal sealed class AzureDocumentIntelligenceExtractionClient : IDocumentExtractionClient
{
    private readonly DocumentIntelligenceClient _client;
    private readonly DocumentExtractionClientMetadata _metadata;
    private bool _disposed;

    public AzureDocumentIntelligenceExtractionClient(Uri endpoint, string key)
        : this(AzureDocumentIntelligenceReader.CreateClient(endpoint, key), endpoint)
    {
    }

    internal AzureDocumentIntelligenceExtractionClient(DocumentIntelligenceClient client, Uri? endpoint = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _metadata = new("Azure.AI.DocumentIntelligence", endpoint, "prebuilt-layout");
    }

    public async Task<DocumentExtractionResult> ExtractAsync(
        Stream document, string mediaType, DocumentExtractionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (options?.ModelId is { } modelId)
            ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        var result = await AzureDocumentIntelligenceReader.AnalyzeAsync(
            _client, document, mediaType, options?.ModelId ?? "prebuilt-layout", cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return MapResult(result, cancellationToken);
    }

    public async IAsyncEnumerable<DocumentExtractionPageResult> ExtractPagesAsync(
        Stream document, string mediaType, DocumentExtractionOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // The service completes one analysis before its pages can be enumerated.
        var result = await ExtractAsync(document, mediaType, options, cancellationToken);
        for (var index = 0; index < result.Pages.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new(result.Pages[index])
            {
                PagesProcessed = index + 1,
                TotalPages = result.Pages.Count,
                RawRepresentation = result.Pages[index].RawRepresentation,
            };
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (serviceKey is not null)
            return null;
        if (serviceType == typeof(DocumentExtractionClientMetadata))
            return _metadata;
        if (serviceType.IsInstanceOfType(this))
            return this;
        return serviceType.IsInstanceOfType(_client) ? _client : null;
    }

    public void Dispose() => _disposed = true;

    internal static DocumentExtractionResult MapResult(AnalyzeResult result, CancellationToken token = default)
    {
        var elements = new List<(int Page, int Offset, int Order, DocumentElement Element)>();
        var tables = result.Tables ?? [];
        var order = 0;
        foreach (var paragraph in result.Paragraphs ?? [])
        {
            token.ThrowIfCancellationRequested();
            var number = AzureDocumentIntelligenceReader.GetPage(paragraph.BoundingRegions, paragraph.Spans, result.Pages);
            if (paragraph.Spans is { Count: > 0 } && tables.Any(table =>
                AzureDocumentIntelligenceReader.GetTablePage(table, result.Pages) == number &&
                paragraph.Spans.All(span => table.Spans.Any(tableSpan =>
                    span.Offset >= tableSpan.Offset &&
                    (long)span.Offset + span.Length <= (long)tableSpan.Offset + tableSpan.Length))))
                continue;
            elements.Add((number, AzureDocumentIntelligenceReader.GetOffset(paragraph.Spans), order++,
                new DocumentBlock(paragraph.Content ?? "")
                {
                    Kind = paragraph.Role == ParagraphRole.Title ? DocumentBlockKind.Title :
                        paragraph.Role is null ? DocumentBlockKind.Paragraph : null,
                    BoundingRegion = Region(paragraph.BoundingRegions),
                    RawRepresentation = paragraph,
                }));
        }
        foreach (var table in tables)
        {
            token.ThrowIfCancellationRequested();
            AzureDocumentIntelligenceReader.ValidateTable(table, token);
            var cells = new List<ExtractionCell>();
            foreach (var cell in table.Cells)
            {
                token.ThrowIfCancellationRequested();
                var nested = (result.Paragraphs ?? []).Where(paragraph =>
                    paragraph.Spans is { Count: > 0 } && paragraph.Spans.All(span =>
                        cell.Spans.Any(cellSpan => span.Offset >= cellSpan.Offset &&
                            (long)span.Offset + span.Length <= (long)cellSpan.Offset + cellSpan.Length)))
                    .OrderBy(paragraph => AzureDocumentIntelligenceReader.GetOffset(paragraph.Spans))
                    .Select(paragraph => (DocumentElement)new DocumentBlock(paragraph.Content ?? "")
                    {
                        Kind = paragraph.Role == ParagraphRole.Title ? DocumentBlockKind.Title :
                            paragraph.Role is null ? DocumentBlockKind.Paragraph : null,
                        BoundingRegion = Region(paragraph.BoundingRegions),
                        RawRepresentation = paragraph,
                    }).ToArray();
                cells.Add(new(cell.RowIndex, cell.ColumnIndex, cell.Content ?? "")
                {
                    RowSpan = cell.RowSpan ?? 1,
                    ColumnSpan = cell.ColumnSpan ?? 1,
                    Kind = cell.Kind == AzureCellKind.ColumnHeader ? ExtractionCellKind.ColumnHeader :
                        cell.Kind == AzureCellKind.RowHeader ? ExtractionCellKind.RowHeader :
                        cell.Kind == AzureCellKind.Content ? ExtractionCellKind.Content : null,
                    Elements = nested.Length > 0 ? nested : null,
                    BoundingRegion = Region(cell.BoundingRegions),
                    RawRepresentation = cell,
                });
            }
            elements.Add((AzureDocumentIntelligenceReader.GetTablePage(table, result.Pages),
                AzureDocumentIntelligenceReader.GetOffset(table.Spans), order++,
                new ExtractionTable(table.RowCount, table.ColumnCount, cells)
                {
                    BoundingRegion = Region(table.BoundingRegions),
                    RawRepresentation = table,
                }));
        }
        foreach (var figure in result.Figures ?? [])
        {
            token.ThrowIfCancellationRequested();
            elements.Add((AzureDocumentIntelligenceReader.GetPage(figure.BoundingRegions, figure.Spans, result.Pages),
                AzureDocumentIntelligenceReader.GetOffset(figure.Spans), order++,
                new DocumentImage
                {
                    Caption = figure.Caption?.Content,
                    BoundingRegion = Region(figure.BoundingRegions),
                    RawRepresentation = figure,
                }));
        }

        var pages = new List<ExtractionPage>();
        foreach (var page in result.Pages.OrderBy(page => page.PageNumber))
        {
            token.ThrowIfCancellationRequested();
            var pageElements = elements.Where(item => item.Page == page.PageNumber)
                .OrderBy(item => item.Offset).ThenBy(item => item.Order).Select(item => item.Element).ToList();
            // Lines are a fallback only: paragraph projections would duplicate their text.
            if (pageElements.Count == 0)
                foreach (var line in page.Lines ?? [])
                    pageElements.Add(new DocumentBlock(line.Content ?? "")
                    {
                        Kind = DocumentBlockKind.Paragraph,
                        BoundingRegion = Polygon(page.PageNumber, line.Polygon),
                        RawRepresentation = line,
                    });
            var text = page.Spans.Count > 0
                ? string.Join("", page.Spans.Select(span => Slice(result.Content, span)))
                : string.Join("\n", page.Lines?.Select(line => line.Content) ?? []);
            pages.Add(new(page.PageNumber, text)
            {
                Elements = pageElements,
                Dimensions = page.Width is { } width && page.Height is { } height
                    ? new DocumentPageDimensions(width, height) : null,
                CoordinateUnit = page.Unit == LengthUnit.Inch ? DocumentCoordinateUnit.Inch :
                    page.Unit == LengthUnit.Pixel ? DocumentCoordinateUnit.Pixel : null,
                CoordinateOrigin = DocumentCoordinateOrigin.TopLeft,
                RawRepresentation = page,
            });
        }
        return new(pages) { RawRepresentation = result };
    }

    private static string Slice(string content, DocumentSpan span)
    {
        if (span.Offset < 0 || span.Length < 0 || (long)span.Offset + span.Length > content.Length)
            throw new InvalidDataException("Cloud returned an invalid document span.");
        return content.Substring(span.Offset, span.Length);
    }

    private static DocumentBoundingRegion? Region(IReadOnlyList<BoundingRegion>? regions) =>
        regions is { Count: > 0 } ? Polygon(regions[0].PageNumber, regions[0].Polygon) : null;

    private static DocumentBoundingRegion? Polygon(int number, IReadOnlyList<float>? coordinates)
    {
        if (coordinates is null || coordinates.Count == 0)
            return null;
        if (coordinates.Count % 2 != 0 || coordinates.Any(value => !float.IsFinite(value)))
            throw new InvalidDataException("Cloud returned invalid geometry.");
        return new(number, Enumerable.Range(0, coordinates.Count / 2)
            .Select(index => new DocumentPoint(coordinates[index * 2], coordinates[index * 2 + 1])).ToArray());
    }
}
