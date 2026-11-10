using Azure.AI.DocumentIntelligence;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;

namespace AIExtensions.Sample.ChatPlayground;

internal sealed class AzureDocumentIntelligenceExtractionClient : DocumentExtractionClient
{
    private readonly DocumentIntelligenceClient _client;
    private bool _disposed;

    public AzureDocumentIntelligenceExtractionClient(Uri endpoint, string key)
        : this(AzureDocumentIntelligenceReader.CreateClient(endpoint, key))
    {
    }

    internal AzureDocumentIntelligenceExtractionClient(DocumentIntelligenceClient client, Uri? endpoint = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public override Task<ExtractionDocument> ExtractAsync(
        Stream document, string identifier, string mediaType, DocumentExtractionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        var request = options?.Clone();
        if (request?.AdditionalProperties.Count > 0)
            throw new NotSupportedException("This Azure Document Intelligence adapter does not support additional request options.");
        return ExtractCoreAsync(document, identifier, mediaType, request?.ExtractorName ?? "prebuilt-layout",
            cancellationToken);
    }

    private async Task<ExtractionDocument> ExtractCoreAsync(
        Stream document, string identifier, string mediaType, string modelId, CancellationToken cancellationToken)
    {
        var native = await AzureDocumentIntelligenceReader.AnalyzeAsync(
            _client, document, mediaType, modelId, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return MapResult(native, identifier, cancellationToken);
    }

    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (serviceKey is not null)
            return null;
        if (serviceType.IsInstanceOfType(this))
            return this;
        return serviceType.IsInstanceOfType(_client) ? _client : null;
    }

    protected override void Dispose(bool disposing) => _disposed = true;

    internal static ExtractionDocument MapResult(
        AnalyzeResult result, string identifier = "document", CancellationToken token = default)
    {
        var document = new ExtractionDocument(identifier) { RawRepresentation = result };
        var frames = result.Pages.ToDictionary(page => page.PageNumber, page => new ExtractionDocumentSourcePage(
            $"{identifier}/page/{page.PageNumber}", page.PageNumber,
            page.Width is > 0 && page.Height is > 0 ? new(page.Width.Value, page.Height.Value) : null,
            page.Unit == LengthUnit.Inch ? ExtractionDocumentCoordinateUnit.Inch :
                page.Unit == LengthUnit.Pixel ? ExtractionDocumentCoordinateUnit.Pixel : null,
            ExtractionDocumentCoordinateOrigin.TopLeft));
        document.SourcePages.AddRange(frames.Values);
        var elements = new List<(int Page, int Offset, int Order, ExtractionDocumentElement Element)>();
        var tables = result.Tables ?? [];
        var order = 0;
        foreach (var paragraph in result.Paragraphs ?? [])
        {
            token.ThrowIfCancellationRequested();
            var number = AzureDocumentIntelligenceReader.GetPage(paragraph.BoundingRegions, paragraph.Spans, result.Pages);
            if (paragraph.Spans is { Count: > 0 } && tables.Any(table =>
                AzureDocumentIntelligenceReader.GetTablePage(table, result.Pages) == number &&
                paragraph.Spans.All(span => table.Spans.Any(tableSpan => Contains(tableSpan, span)))))
                continue;
            elements.Add((number, AzureDocumentIntelligenceReader.GetOffset(paragraph.Spans), order++, Text(paragraph)));
        }
        foreach (var table in tables)
        {
            token.ThrowIfCancellationRequested();
            AzureDocumentIntelligenceReader.ValidateTable(table, token);
            var number = AzureDocumentIntelligenceReader.GetTablePage(table, result.Pages);
            var mapped = new ExtractionDocumentTableElement(null, table.RowCount, table.ColumnCount);
            mapped.Sources.AddRange(Sources(table.BoundingRegions, number, table));
            foreach (var cell in table.Cells)
            {
                token.ThrowIfCancellationRequested();
                var nested = (result.Paragraphs ?? []).Where(paragraph =>
                    paragraph.Spans is { Count: > 0 } &&
                    paragraph.Spans.All(span => cell.Spans.Any(cellSpan => Contains(cellSpan, span))))
                    .OrderBy(paragraph => AzureDocumentIntelligenceReader.GetOffset(paragraph.Spans)).ToArray();
                ExtractionDocumentElement? content = null;
                if (nested.Length > 0)
                {
                    var section = new ExtractionDocumentSection();
                    section.Elements.AddRange(nested.Select(Text));
                    content = section;
                }
                else if (cell.Content is { Length: > 0 } text)
                    content = new ExtractionDocumentTextElement(new TextContent(text) { RawRepresentation = cell });
                var mappedCell = content is null
                    ? new ExtractionDocumentTableCell(cell.RowSpan ?? 1, cell.ColumnSpan ?? 1, cell.Kind?.ToString())
                    : new ExtractionDocumentTableCell(content, cell.RowSpan ?? 1, cell.ColumnSpan ?? 1, cell.Kind?.ToString());
                mappedCell.Sources.AddRange(Sources(cell.BoundingRegions, number, cell));
                mapped.Cells[cell.RowIndex, cell.ColumnIndex] = mappedCell;
            }
            elements.Add((number, AzureDocumentIntelligenceReader.GetOffset(table.Spans), order++, mapped));
        }
        foreach (var figure in result.Figures ?? [])
        {
            token.ThrowIfCancellationRequested();
            var number = AzureDocumentIntelligenceReader.GetPage(figure.BoundingRegions, figure.Spans, result.Pages);
            var image = new ExtractionDocumentImageElement { AlternativeText = figure.Caption?.Content };
            image.Sources.AddRange(Sources(figure.BoundingRegions, number, figure));
            elements.Add((number, AzureDocumentIntelligenceReader.GetOffset(figure.Spans), order++, image));
        }
        foreach (var page in result.Pages.OrderBy(page => page.PageNumber))
        {
            token.ThrowIfCancellationRequested();
            var section = new ExtractionDocumentSection();
            section.Sources.Add(new(frames[page.PageNumber], rawRepresentation: page));
            section.Elements.AddRange(elements.Where(item => item.Page == page.PageNumber)
                .OrderBy(item => item.Offset).ThenBy(item => item.Order).Select(item => item.Element));
            if (section.Elements.Count == 0)
            {
                foreach (var line in page.Lines ?? [])
                {
                    token.ThrowIfCancellationRequested();
                    var text = new ExtractionDocumentTextElement(new TextContent(line.Content ?? "") { RawRepresentation = line });
                    text.Sources.Add(Source(page.PageNumber, line.Polygon, line));
                    section.Elements.Add(text);
                }
                if (section.Elements.Count == 0 && page.Spans.Count > 0)
                {
                    var text = string.Join("", page.Spans.Select(span => Slice(result.Content, span)));
                    if (text.Length > 0)
                    {
                        section.Content = new TextContent(text) { RawRepresentation = page };
                    }
                }
            }
            document.Sections.Add(section);
        }
        token.ThrowIfCancellationRequested();
        return document;

        ExtractionDocumentTextElement Text(DocumentParagraph paragraph)
        {
            var role = paragraph.Role == ParagraphRole.Title || paragraph.Role == ParagraphRole.SectionHeading
                ? "heading" : paragraph.Role?.ToString();
            var text = new ExtractionDocumentTextElement(
                new TextContent(paragraph.Content ?? "") { RawRepresentation = paragraph }, role);
            text.Sources.AddRange(Sources(paragraph.BoundingRegions,
                AzureDocumentIntelligenceReader.GetPage(paragraph.BoundingRegions, paragraph.Spans, result.Pages), paragraph));
            return text;
        }

        IEnumerable<ExtractionDocumentSourceReference> Sources(IReadOnlyList<BoundingRegion>? regions, int number, object raw) =>
            regions is { Count: > 0 }
                ? regions.Select(region => Source(region.PageNumber, region.Polygon, raw))
                : [Source(number, null, raw)];

        ExtractionDocumentSourceReference Source(int number, IReadOnlyList<float>? coordinates, object raw)
        {
            if (!frames.TryGetValue(number, out var frame))
                throw new InvalidDataException("Cloud returned an unknown source page.");
            if (coordinates is null || coordinates.Count == 0)
                return new(frame, rawRepresentation: raw);
            if (coordinates.Count < 6 || coordinates.Count % 2 != 0 || coordinates.Any(value => !float.IsFinite(value)))
                throw new InvalidDataException("Cloud returned invalid geometry.");
            return new(frame, Enumerable.Range(0, coordinates.Count / 2)
                .Select(index => new ExtractionDocumentPoint(coordinates[index * 2], coordinates[index * 2 + 1])), raw);
        }
    }

    private static bool Contains(DocumentSpan outer, DocumentSpan inner) =>
        inner.Offset >= outer.Offset && (long)inner.Offset + inner.Length <= (long)outer.Offset + outer.Length;

    private static string Slice(string content, DocumentSpan span)
    {
        if (span.Offset < 0 || span.Length < 0 || (long)span.Offset + span.Length > content.Length)
            throw new InvalidDataException("Cloud returned an invalid document span.");
        return content.Substring(span.Offset, span.Length);
    }
}
