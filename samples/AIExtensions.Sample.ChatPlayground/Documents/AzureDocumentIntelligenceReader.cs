using System.Net;
using System.Text;
using Azure;
using Azure.AI.DocumentIntelligence;
using Microsoft.Extensions.DataIngestion;
using AzureDocumentPage = Azure.AI.DocumentIntelligence.DocumentPage;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Sample-only Azure layout reader; never used as an on-device fallback.</summary>
internal sealed class AzureDocumentIntelligenceReader : IngestionDocumentReader
{
    private const int MaximumBytes = 20 * 1024 * 1024;
    private readonly DocumentIntelligenceClient _client;

    public AzureDocumentIntelligenceReader(Uri endpoint, string key)
    {
        if (!endpoint.IsAbsoluteUri || endpoint.Scheme != Uri.UriSchemeHttps ||
            endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0 ||
            endpoint.AbsolutePath != "/")
            throw new InvalidOperationException("Document Intelligence endpoint must be an HTTPS resource root URL.");
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _client = new DocumentIntelligenceClient(endpoint, new AzureKeyCredential(key));
    }

    public override async Task<IngestionDocument> ReadAsync(
        Stream source, string identifier, string mediaType, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(identifier);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        if (!source.CanRead)
            throw new ArgumentException("The document stream must be readable.", nameof(source));
        if (mediaType is not ("application/pdf" or "image/png" or "image/jpeg" or "image/heic" or "image/tiff"))
            throw new NotSupportedException($"Unsupported document media type: {mediaType}.");

        await using var content = new MemoryStream();
        var buffer = new byte[81920];
        int count;
        while ((count = await source.ReadAsync(buffer, cancellationToken)) != 0)
        {
            if (content.Length + count > MaximumBytes)
                throw new InvalidOperationException("Documents must be 20 MB or smaller.");
            await content.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
        }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(100));
        var operation = await _client.AnalyzeDocumentAsync(
            WaitUntil.Completed, "prebuilt-layout", BinaryData.FromBytes(content.ToArray()), deadline.Token);
        deadline.Token.ThrowIfCancellationRequested();
        return MapResult(operation.Value, identifier);
    }

    internal static IngestionDocument MapResult(AnalyzeResult result, string identifier)
    {
        var document = new IngestionDocument(identifier);
        var sections = new SortedDictionary<int, IngestionDocumentSection>();
        var elements = new List<(int Page, int Offset, int Order, IngestionDocumentElement Element)>();
        foreach (var page in result.Pages)
            GetSection(page.PageNumber);

        var tables = result.Tables ?? [];
        var order = 0;
        foreach (var paragraph in result.Paragraphs ?? [])
        {
            if (string.IsNullOrWhiteSpace(paragraph.Content))
                    continue;
            var pageNumber = GetPage(paragraph.BoundingRegions, paragraph.Spans, result.Pages);
            if (paragraph.Spans is { Count: > 0 } && tables.Any(table =>
                    GetTablePage(table, result.Pages) == pageNumber && table.Spans is { Count: > 0 } &&
                    paragraph.Spans.All(span => table.Spans.Any(tableSpan =>
                        span.Offset >= tableSpan.Offset &&
                        (long)span.Offset + span.Length <= (long)tableSpan.Offset + tableSpan.Length))))
                    continue;

            IngestionDocumentElement element = paragraph.Role == ParagraphRole.Title
                    ? new IngestionDocumentHeader(paragraph.Content) { Level = 1 }
                    : paragraph.Role == ParagraphRole.SectionHeading
                        ? new IngestionDocumentHeader(paragraph.Content) { Level = 2 }
                    : new IngestionDocumentParagraph(paragraph.Content);
            element.Text = paragraph.Content;
            elements.Add((pageNumber, GetOffset(paragraph.Spans), order++, element));
        }

        foreach (var table in tables)
        {
            var rows = table.RowCount;
            var columns = table.ColumnCount;
            if (rows <= 0 || columns <= 0 || rows > 1000 || columns > 100)
                    throw new InvalidDataException("Cloud returned invalid table dimensions.");

            var cells = new IngestionDocumentElement?[rows, columns];
            var positions = new Dictionary<(int Row, int Column), DocumentTableCell>();
            var occupied = new bool[rows, columns];
            var pageNumber = GetTablePage(table, result.Pages);
            foreach (var cell in table.Cells)
            {
                    var rowSpan = cell.RowSpan ?? 1;
                    var columnSpan = cell.ColumnSpan ?? 1;
                    if (cell.RowIndex < 0 || cell.ColumnIndex < 0 || rowSpan <= 0 || columnSpan <= 0 ||
                        (long)cell.RowIndex + rowSpan > rows || (long)cell.ColumnIndex + columnSpan > columns)
                        throw new InvalidDataException("Cloud returned a table cell outside its dimensions.");
                    for (var row = cell.RowIndex; row < cell.RowIndex + rowSpan; row++)
                        for (var column = cell.ColumnIndex; column < cell.ColumnIndex + columnSpan; column++)
                        {
                            if (occupied[row, column])
                                throw new InvalidDataException("Cloud returned overlapping table cells.");
                            occupied[row, column] = true;
                        }
                    positions.Add((cell.RowIndex, cell.ColumnIndex), cell);
                    var content = cell.Content ?? "";
                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        var element = new IngestionDocumentParagraph(content) { Text = content };
                        cells[cell.RowIndex, cell.ColumnIndex] = element;
                    }
            }

            var markdown = new StringBuilder("<table>");
            var text = new StringBuilder();
            for (var row = 0; row < rows; row++)
            {
                    markdown.Append("<tr>");
                    for (var column = 0; column < columns; column++)
                    {
                        if (positions.TryGetValue((row, column), out var cell))
                        {
                            var header = cell.Kind == DocumentTableCellKind.ColumnHeader ||
                                cell.Kind == DocumentTableCellKind.RowHeader ||
                                cell.Kind == DocumentTableCellKind.StubHead;
                            var tag = header ? "th" : "td";
                            markdown.Append('<').Append(tag);
                            if (cell.RowSpan is > 1)
                                markdown.Append(" rowspan=\"").Append(cell.RowSpan.Value).Append('"');
                            if (cell.ColumnSpan is > 1)
                                markdown.Append(" colspan=\"").Append(cell.ColumnSpan.Value).Append('"');
                            markdown.Append('>').Append(WebUtility.HtmlEncode(cell.Content ?? ""))
                                .Append("</").Append(tag).Append('>');
                            if (text.Length > 0)
                                text.Append(' ');
                            text.Append(cell.Content);
                        }
                        else if (!occupied[row, column])
                            markdown.Append("<td></td>");
                    }
                    markdown.Append("</tr>");
            }
            markdown.Append("</table>");
            elements.Add((pageNumber, GetOffset(table.Spans), order++,
                    new IngestionDocumentTable(markdown.ToString(), cells) { Text = text.ToString() }));
        }

        foreach (var item in elements.OrderBy(item => item.Page).ThenBy(item => item.Offset).ThenBy(item => item.Order))
            GetSection(item.Page).Elements.Add(item.Element);
        foreach (var section in sections.Values)
            document.Sections.Add(section);
        return document;

        IngestionDocumentSection GetSection(int number)
        {
            if (!sections.TryGetValue(number, out var section))
                sections[number] = section = new IngestionDocumentSection { PageNumber = number };
            return section;
        }
    }

    private static int GetPage(
        IReadOnlyList<BoundingRegion>? regions, IReadOnlyList<DocumentSpan>? spans, IReadOnlyList<AzureDocumentPage> pages)
    {
        if (regions is { Count: > 0 })
            return regions[0].PageNumber;
        var offset = GetOffset(spans);
        foreach (var page in pages)
            if (page.Spans.Any(span => offset >= span.Offset && (long)offset < (long)span.Offset + span.Length))
                return page.PageNumber;
        return pages.Count > 0 ? pages[0].PageNumber : 1;
    }

    private static int GetTablePage(DocumentTable table, IReadOnlyList<AzureDocumentPage> pages) =>
        table.BoundingRegions is { Count: > 0 } ? GetPage(table.BoundingRegions, table.Spans, pages) :
        table.Cells.Select(cell => cell.BoundingRegions).FirstOrDefault(regions => regions is { Count: > 0 }) is { } cellRegions
            ? GetPage(cellRegions, table.Spans, pages)
            : GetPage(null, table.Spans, pages);

    private static int GetOffset(IReadOnlyList<DocumentSpan>? spans) =>
        spans is { Count: > 0 } ? spans.Min(span => span.Offset) : int.MaxValue;
}
