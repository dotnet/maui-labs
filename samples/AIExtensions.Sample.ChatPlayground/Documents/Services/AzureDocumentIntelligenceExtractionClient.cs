using System.Runtime.CompilerServices;
using Azure.AI.DocumentIntelligence;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;

namespace AIExtensions.Sample.ChatPlayground;

internal sealed class AzureDocumentIntelligenceExtractionClient : IDocumentExtractionClient
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

    public IAsyncEnumerable<DocumentExtractionPage> ExtractPagesAsync(
        Stream document, string identifier, string mediaType, DocumentExtractionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        var request = options?.Clone();
        if (request?.AdditionalProperties.Count > 0)
            throw new NotSupportedException("This Azure Document Intelligence adapter does not support additional request options.");
        return ExtractCoreAsync(document, identifier, mediaType, request?.ModelId ?? "prebuilt-layout",
            request?.CaptureRawOutput ?? true, cancellationToken);
    }

    private async IAsyncEnumerable<DocumentExtractionPage> ExtractCoreAsync(
        Stream document, string identifier, string mediaType, string modelId, bool capture,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var native = await AzureDocumentIntelligenceReader.AnalyzeAsync(
            _client, document, mediaType, modelId, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var page in MapPages(native, identifier, modelId, capture, cancellationToken))
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
        if (serviceType.IsInstanceOfType(this))
            return this;
        return serviceType.IsInstanceOfType(_client) ? _client : null;
    }

    public void Dispose() => _disposed = true;

    internal static DocumentExtractionResult MapResult(
        AnalyzeResult result, string identifier = "document", bool captureRawOutput = true,
        CancellationToken token = default) =>
        MapPages(result, identifier, result.ModelId, captureRawOutput, token)
            .ToDocumentExtractionResult(identifier, captureRawOutput);

    private static IReadOnlyList<DocumentExtractionPage> MapPages(
        AnalyzeResult result, string identifier, string? modelId, bool capture, CancellationToken token)
    {
        var output = new DocumentSourceOutput($"{identifier}/azure/analysis",
            rawRepresentation: capture ? result : null, captureDisabled: !capture,
            providerName: "Azure.AI.DocumentIntelligence.SDK", modelId: modelId);
        var frames = result.Pages.ToDictionary(page => page.PageNumber, page => new DocumentSourcePage(
            $"{identifier}/page/{page.PageNumber}", page.PageNumber,
            page.Width is > 0 && page.Height is > 0 ? new(page.Width.Value, page.Height.Value) : null,
            page.Unit == LengthUnit.Inch ? DocumentCoordinateUnit.Inch :
                page.Unit == LengthUnit.Pixel ? DocumentCoordinateUnit.Pixel : null,
            DocumentCoordinateOrigin.TopLeft, output));
        var elements = new List<(int Page, int Offset, int Order, DocumentExtractionObservation Element)>();
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
            elements.Add((number, AzureDocumentIntelligenceReader.GetOffset(paragraph.Spans), order,
                Text(paragraph, $"{identifier}/azure/paragraph/{order++}",
                    AzureDocumentIntelligenceReader.GetOffset(paragraph.Spans))));
        }
        foreach (var table in tables)
        {
            token.ThrowIfCancellationRequested();
            AzureDocumentIntelligenceReader.ValidateTable(table, token);
            var id = $"{identifier}/azure/table/{order}";
            var cells = new List<DocumentExtractionTableCell>();
            foreach (var cell in table.Cells)
            {
                token.ThrowIfCancellationRequested();
                var nested = (result.Paragraphs ?? []).Where(paragraph =>
                    paragraph.Spans is { Count: > 0 } &&
                    paragraph.Spans.All(span => cell.Spans.Any(cellSpan => Contains(cellSpan, span))))
                    .OrderBy(paragraph => AzureDocumentIntelligenceReader.GetOffset(paragraph.Spans)).ToArray();
                var cellAnnotations = new List<DocumentAnnotation>
                {
                    Annotation("azure.documentIntelligence.cell", cell.Content, cell.Kind?.ToString(),
                        cell.BoundingRegions, cell),
                    Spans(cell.Spans),
                };
                foreach (var paragraph in nested)
                {
                    cellAnnotations.Add(Annotation("azure.documentIntelligence.paragraph", paragraph.Content,
                        paragraph.Role?.ToString(), paragraph.BoundingRegions, paragraph));
                    cellAnnotations.Add(Spans(paragraph.Spans));
                }
                var content = cell.Content is { Length: > 0 } text
                    ? new DocumentExtractionText($"{id}/cell/{cell.RowIndex},{cell.ColumnIndex}", 0,
                        DocumentTextKind.Paragraph, text, text) : null;
                cells.Add(new(cell.RowIndex, cell.ColumnIndex, content, cell.RowSpan ?? 1, cell.ColumnSpan ?? 1,
                    cell.Kind?.ToString(), cellAnnotations));
            }
            elements.Add((AzureDocumentIntelligenceReader.GetTablePage(table, result.Pages),
                AzureDocumentIntelligenceReader.GetOffset(table.Spans), order,
                new DocumentExtractionTable(id, AzureDocumentIntelligenceReader.GetOffset(table.Spans), "", table.RowCount, table.ColumnCount, cells,
                    annotations: [Annotation("azure.documentIntelligence.table", regions: table.BoundingRegions, raw: table), Spans(table.Spans)])));
            order++;
        }
        foreach (var figure in result.Figures ?? [])
        {
            token.ThrowIfCancellationRequested();
            var caption = figure.Caption?.Content;
            elements.Add((AzureDocumentIntelligenceReader.GetPage(figure.BoundingRegions, figure.Spans, result.Pages),
                AzureDocumentIntelligenceReader.GetOffset(figure.Spans), order,
                new DocumentExtractionImage($"{identifier}/azure/figure/{order++}", AzureDocumentIntelligenceReader.GetOffset(figure.Spans), "",
                    text: caption, annotations:
                    [Annotation("azure.documentIntelligence.figure", caption, regions: figure.BoundingRegions, raw: figure), Spans(figure.Spans)])));
        }
        var pages = new List<DocumentExtractionPage>();
        foreach (var page in result.Pages.OrderBy(page => page.PageNumber))
        {
            token.ThrowIfCancellationRequested();
            var pageElements = elements.Where(item => item.Page == page.PageNumber)
                .OrderBy(item => item.Offset).ThenBy(item => item.Order).Select(item => item.Element).ToList();
            if (pageElements.Count == 0)
                foreach (var line in page.Lines ?? [])
                    pageElements.Add(new DocumentExtractionText(
                        $"{identifier}/page/{page.PageNumber}/line/{pageElements.Count}", pageElements.Count,
                        DocumentTextKind.Paragraph, line.Content ?? "", line.Content,
                        annotations: [new("azure.documentIntelligence.line", line.Content,
                            regions: Polygon(page.PageNumber, line.Polygon) is { } region ? [region] : null,
                            sourceOutput: output, rawRepresentation: capture ? line : null)]));
            var text = page.Spans.Count > 0 ? string.Join("", page.Spans.Select(span => Slice(result.Content, span))) :
                string.Join("\n", page.Lines?.Select(line => line.Content) ?? []);
            var annotations = new List<DocumentAnnotation>
            {
                new("azure.documentIntelligence.page", text, sourceOutput: output, rawRepresentation: capture ? page : null),
                Spans(page.Spans),
            };
            if (page.Angle is { } angle)
                annotations.Add(new("azure.documentIntelligence.angle", angle));
            foreach (var word in page.Words ?? [])
                annotations.Add(new("azure.documentIntelligence.word", word.Content,
                    regions: Polygon(page.PageNumber, word.Polygon) is { } region ? [region] : null,
                    confidence: new(word.Confidence, "azure.documentIntelligence.confidence", 0, 1),
                    sourceOutput: output, rawRepresentation: capture ? word : null));
            foreach (var mark in page.SelectionMarks ?? [])
                annotations.Add(new("azure.documentIntelligence.selectionMark", mark.State.ToString(),
                    regions: Polygon(page.PageNumber, mark.Polygon) is { } region ? [region] : null,
                    confidence: new(mark.Confidence, "azure.documentIntelligence.confidence", 0, 1),
                    sourceOutput: output, rawRepresentation: capture ? mark : null));
            var barcodeIndex = 0;
            foreach (var barcode in page.Barcodes ?? [])
                pageElements.Add(new DocumentExtractionExtension(
                    $"{identifier}/page/{page.PageNumber}/barcode/{barcodeIndex++}", barcode.Span.Offset,
                    "azure.documentIntelligence.barcode", "", text: barcode.Value,
                    value: new { Kind = barcode.Kind.ToString(), barcode.Value },
                    annotations: [new("azure.documentIntelligence.barcode", barcode.Value,
                        regions: Polygon(page.PageNumber, barcode.Polygon) is { } region ? [region] : null,
                        confidence: new(barcode.Confidence, "azure.documentIntelligence.confidence", 0, 1),
                        sourceOutput: output, rawRepresentation: capture ? barcode : null)]));
            if (pageElements.Count == 0 && text.Length > 0)
                pageElements.Add(new DocumentExtractionText($"{identifier}/page/{page.PageNumber}/content", 0,
                    DocumentTextKind.Paragraph, text, text));
            pages.Add(new(page.PageNumber, pageElements,
                result.ContentFormat == DocumentContentFormat.Markdown ? text : null, sourcePage: frames[page.PageNumber],
                annotations: annotations, sourceOutputs: [output]));
        }
        return pages;

        DocumentExtractionText Text(DocumentParagraph paragraph, string id, int index) =>
            new(id, index, paragraph.Role is { } role && (role == ParagraphRole.Title || role == ParagraphRole.SectionHeading ||
                role == ParagraphRole.PageHeader) ? DocumentTextKind.Header :
                paragraph.Role == ParagraphRole.PageFooter ? DocumentTextKind.Footer : DocumentTextKind.Paragraph,
                paragraph.Content ?? "", paragraph.Content,
                annotations: [Annotation("azure.documentIntelligence.paragraph", paragraph.Content,
                    paragraph.Role?.ToString(), paragraph.BoundingRegions, paragraph), Spans(paragraph.Spans)]);
        DocumentAnnotation Annotation(string kind, object? value = null, string? role = null,
            IReadOnlyList<BoundingRegion>? regions = null, object? raw = null) =>
            new(kind, value, role, regions: regions?.Select(region => Polygon(region.PageNumber, region.Polygon))
                .Where(region => region is not null).Cast<DocumentRegion>().ToArray(),
                sourceOutput: output, rawRepresentation: capture ? raw : null);
        DocumentAnnotation Spans(IReadOnlyList<DocumentSpan> spans) =>
            new("azure.documentIntelligence.spans",
                spans.Select(span => new { span.Offset, span.Length }).ToArray());
        DocumentRegion? Polygon(int number, IReadOnlyList<float>? coordinates)
        {
            if (coordinates is null || coordinates.Count == 0)
                return null;
            if (coordinates.Count < 6 || coordinates.Count % 2 != 0 ||
                coordinates.Any(value => !float.IsFinite(value)) || !frames.TryGetValue(number, out var frame))
                throw new InvalidDataException("Cloud returned invalid geometry.");
            return new(frame, Enumerable.Range(0, coordinates.Count / 2)
                .Select(index => new DocumentPoint(coordinates[index * 2], coordinates[index * 2 + 1])).ToArray());
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
