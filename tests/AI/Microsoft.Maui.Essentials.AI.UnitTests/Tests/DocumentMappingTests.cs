using Azure.AI.DocumentIntelligence;
using Microsoft.Extensions.DataIngestion;
using Xunit;

namespace AIExtensions.Sample.ChatPlayground;

public sealed class DocumentMappingTests
{
    [Fact]
    public void MapResult_InterleavesElementsAndOmitsTableParagraphs()
    {
        var table = DocumentIntelligenceModelFactory.DocumentTable(
            rowCount: 1, columnCount: 2,
            cells:
            [
                DocumentIntelligenceModelFactory.DocumentTableCell(rowIndex: 0, columnIndex: 0, content: "A"),
                DocumentIntelligenceModelFactory.DocumentTableCell(rowIndex: 0, columnIndex: 1, content: "B"),
            ],
            boundingRegions: [Region(1)],
            spans: [Span(20, 10)]);
        var result = DocumentIntelligenceModelFactory.AnalyzeResult(
            pages: [DocumentIntelligenceModelFactory.DocumentPage(pageNumber: 1, spans: [Span(0, 50)]),
                    DocumentIntelligenceModelFactory.DocumentPage(pageNumber: 2, spans: [Span(50, 50)])],
            paragraphs:
            [
                Paragraph("After", 40, 5, 1),
                Paragraph("On page 2", 60, 9, 2, hasRegion: false),
                Paragraph("A B", 21, 3, 1),
                Paragraph("Title", 0, 5, 1, ParagraphRole.Title),
            ],
            tables: [table]);

        var document = AzureDocumentIntelligenceReader.MapResult(result, "layout.pdf");

        Assert.Equal("layout.pdf", document.Identifier);
        Assert.Equal([1, 2], document.Sections.Select(section => section.PageNumber).ToArray());
        Assert.Equal(new[] { "Title", "A B", "After" },
            document.Sections[0].Elements.Select(element => element.Text ?? "").ToArray());
        Assert.IsType<IngestionDocumentHeader>(document.Sections[0].Elements[0]);
        Assert.IsType<IngestionDocumentTable>(document.Sections[0].Elements[1]);
        Assert.Equal("On page 2", Assert.Single(document.Sections[1].Elements).Text);
    }

    [Fact]
    public void MapResult_RendersActualCellKindsAndSpansWithoutInventingHeaders()
    {
        var table = DocumentIntelligenceModelFactory.DocumentTable(
            rowCount: 3, columnCount: 2,
            cells:
            [
                DocumentIntelligenceModelFactory.DocumentTableCell(
                    kind: DocumentTableCellKind.ColumnHeader, rowIndex: 0, columnIndex: 0,
                    columnSpan: 2, content: "Names & roles"),
                DocumentIntelligenceModelFactory.DocumentTableCell(
                    kind: DocumentTableCellKind.RowHeader, rowIndex: 1, columnIndex: 0,
                    rowSpan: 2, content: "Team"),
                DocumentIntelligenceModelFactory.DocumentTableCell(
                    rowIndex: 1, columnIndex: 1, content: "Alex"),
                DocumentIntelligenceModelFactory.DocumentTableCell(
                    rowIndex: 2, columnIndex: 1, content: "Sam"),
            ],
            spans: [Span(0, 22)]);
        var result = DocumentIntelligenceModelFactory.AnalyzeResult(
            pages: [DocumentIntelligenceModelFactory.DocumentPage(pageNumber: 1)],
            tables: [table]);

        var document = AzureDocumentIntelligenceReader.MapResult(result, "table.pdf");
        var mapped = Assert.IsType<IngestionDocumentTable>(Assert.Single(document.Sections[0].Elements));
        var markdown = mapped.GetMarkdown();

        Assert.Contains("<th colspan=\"2\">Names &amp; roles</th>", markdown);
        Assert.Contains("<th rowspan=\"2\">Team</th>", markdown);
        Assert.Contains("<td>Alex</td>", markdown);
        Assert.Contains("<td>Sam</td>", markdown);
        Assert.DoesNotContain("---", markdown);
        Assert.Null(mapped.Cells[0, 1]);
        Assert.Null(mapped.Cells[2, 0]);
        Assert.Equal("Names & roles", mapped.Cells[0, 0]!.Text);
        Assert.Equal("Team", mapped.Cells[1, 0]!.Text);
    }

    [Fact]
    public void MapResult_FirstRowContentCell_RemainsDataNotHeader()
    {
        var table = DocumentIntelligenceModelFactory.DocumentTable(
            rowCount: 1, columnCount: 1,
            cells: [DocumentIntelligenceModelFactory.DocumentTableCell(
                kind: DocumentTableCellKind.Content, content: "Value")]);
        var result = DocumentIntelligenceModelFactory.AnalyzeResult(
            pages: [DocumentIntelligenceModelFactory.DocumentPage(pageNumber: 1)],
            tables: [table]);

        var document = AzureDocumentIntelligenceReader.MapResult(result, "data.pdf");
        var markdown = Assert.IsType<IngestionDocumentTable>(
            Assert.Single(document.Sections[0].Elements)).GetMarkdown();

        Assert.Contains("<td>Value</td>", markdown);
        Assert.DoesNotContain("<th", markdown);
    }

    [Fact]
    public void MapResult_OverlappingTableCells_RejectsInvalidLayout()
    {
        var table = DocumentIntelligenceModelFactory.DocumentTable(
            rowCount: 1, columnCount: 2,
            cells:
            [
                DocumentIntelligenceModelFactory.DocumentTableCell(rowIndex: 0, columnIndex: 0, columnSpan: 2, content: "Merged"),
                DocumentIntelligenceModelFactory.DocumentTableCell(rowIndex: 0, columnIndex: 1, content: "Overlaps"),
            ]);
        var result = DocumentIntelligenceModelFactory.AnalyzeResult(
            pages: [DocumentIntelligenceModelFactory.DocumentPage(pageNumber: 1)],
            tables: [table]);

        Assert.Throws<InvalidDataException>(() => AzureDocumentIntelligenceReader.MapResult(result, "invalid.pdf"));
    }

    private static DocumentParagraph Paragraph(
        string content, int offset, int length, int page, ParagraphRole? role = null, bool hasRegion = true) =>
        DocumentIntelligenceModelFactory.DocumentParagraph(
            role: role, content: content,
            boundingRegions: hasRegion ? [Region(page)] : [],
            spans: [Span(offset, length)]);

    private static BoundingRegion Region(int page) =>
        DocumentIntelligenceModelFactory.BoundingRegion(pageNumber: page);

    private static DocumentSpan Span(int offset, int length) =>
        DocumentIntelligenceModelFactory.DocumentSpan(offset: offset, length: length);
}
