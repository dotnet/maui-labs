using Azure.AI.DocumentIntelligence;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.Documents;
using Xunit;
using static AIExtensions.Sample.ChatPlayground.CloudDocumentTestFixtures;

namespace AIExtensions.Sample.ChatPlayground;

public sealed class DocumentMappingTests
{
    [Fact]
    public async Task ReadAsync_UsesToolkitNativePointersToKeepTableParagraphsInsideCells()
    {
        var result = DocumentIntelligenceModelFactory.AnalyzeResult(content: "TitleABAfter",
            pages: [DocumentIntelligenceModelFactory.DocumentPage(pageNumber: 1, spans: [Span(0, 12)]),
                DocumentIntelligenceModelFactory.DocumentPage(pageNumber: 3)],
            paragraphs:
            [
                Paragraph("After", 7, 5),
                Paragraph("A", 5, 1),
                Paragraph("Title", 0, 5, ParagraphRole.Title),
            ],
            tables: [DocumentIntelligenceModelFactory.DocumentTable(rowCount: 1, columnCount: 2,
                boundingRegions: [Region(1)], spans: [Span(5, 2)], cells:
                [
                    DocumentIntelligenceModelFactory.DocumentTableCell(content: "A", boundingRegions: [Region(1)],
                        elements: ["/paragraphs/1"]),
                    DocumentIntelligenceModelFactory.DocumentTableCell(columnIndex: 1, content: "B", boundingRegions: [Region(1)]),
                ])]);
        using var extraction = new AzureDocumentIntelligenceExtractionClient(AzureSdk(result));
        using var input = new MemoryStream([1]);
        var document = await new AzureDocumentIntelligenceReader(extraction)
            .ReadAsync(input, "layout.pdf", "application/pdf");
        Assert.Equal("layout.pdf", document.Identifier);
        Assert.Equal([1, 3], document.Sections.Select(section => section.PageNumber));
        var elements = document.Sections[0].Elements;
        Assert.Equal("Title", elements[0].Text);
        Assert.IsType<IngestionDocumentParagraph>(elements[0]);
        var table = Assert.IsType<IngestionDocumentTable>(elements[1]);
        Assert.Equal("A", Assert.Single(Assert.IsType<IngestionDocumentSection>(table.Cells[0, 0]).Elements).Text);
        Assert.Equal("B", table.Cells[0, 1]!.Text);
        Assert.Equal("After", elements[2].Text);
        Assert.Equal(3, elements.Count);
        Assert.Empty(document.Sections[1].Elements);
        Assert.True(input.CanRead);
    }

    [Fact]
    public async Task ReadAsync_RendersReportedCellKindsAndSpansWithoutInventingHeaders()
    {
        var native = DocumentIntelligenceModelFactory.AnalyzeResult(
            pages: [DocumentIntelligenceModelFactory.DocumentPage(pageNumber: 1)],
            tables: [DocumentIntelligenceModelFactory.DocumentTable(rowCount: 3, columnCount: 2,
                boundingRegions: [Region(1)], cells:
                [
                    DocumentIntelligenceModelFactory.DocumentTableCell(kind: DocumentTableCellKind.ColumnHeader,
                        columnSpan: 2, content: "Names & roles", boundingRegions: [Region(1)]),
                    DocumentIntelligenceModelFactory.DocumentTableCell(kind: DocumentTableCellKind.RowHeader,
                        rowIndex: 1, rowSpan: 2, content: "Team", boundingRegions: [Region(1)]),
                    DocumentIntelligenceModelFactory.DocumentTableCell(rowIndex: 1, columnIndex: 1,
                        kind: DocumentTableCellKind.Content, content: "Alex", boundingRegions: [Region(1)]),
                    DocumentIntelligenceModelFactory.DocumentTableCell(rowIndex: 2, columnIndex: 1,
                        content: "Sam", boundingRegions: [Region(1)]),
                ])]);
        using var extraction = new AzureDocumentIntelligenceExtractionClient(AzureSdk(native));
        using var input = new MemoryStream([1]);
        var document = await new AzureDocumentIntelligenceReader(extraction)
            .ReadAsync(input, "table.pdf", "application/pdf");
        var table = Assert.IsType<IngestionDocumentTable>(Assert.Single(document.Sections[0].Elements));
        Assert.Contains("<th colspan=\"2\">Names &amp; roles</th>", table.GetMarkdown());
        Assert.Contains("<th rowspan=\"2\">Team</th>", table.GetMarkdown());
        Assert.Contains("<td>Alex</td>", table.GetMarkdown());
        Assert.Contains("<td>Sam</td>", table.GetMarkdown());
        Assert.DoesNotContain("---", table.GetMarkdown());
        Assert.Null(table.Cells[0, 1]);
        Assert.Null(table.Cells[2, 0]);
        Assert.Equal("Names & roles", table.Cells[0, 0]!.Text);
        Assert.Equal(1, table.Cells[0, 0]!.PageNumber);
        Assert.False(table.HasMetadata);
    }

    [Fact]
    public void Project_PreservesSectionsLocalPayloadNestedTablesAndImageCaption()
    {
        var page = new ExtractionDocumentSourcePage("page", 4);
        var document = new ExtractionDocument("source");
        document.SourcePages.Add(page);
        var section = new ExtractionDocumentSection { Content = new TextContent("Local") };
        section.Sources.Add(new(page));
        document.Sections.Add(section);
        var nested = new ExtractionDocumentSection();
        nested.Elements.Add(new ExtractionDocumentTextElement(new TextContent("Measured heading"), "heading") { HeadingLevel = 2 });
        var table = new ExtractionDocumentTableElement(null, 1, 1);
        var inner = new ExtractionDocumentTableElement(new TextContent("Native HTML"), 0, 0);
        table.Cells[0, 0] = new ExtractionDocumentTableCell(inner);
        nested.Elements.Add(table);
        nested.Elements.Add(new ExtractionDocumentImageElement
        {
            Content = new DataContent(new byte[] { 1, 2 }, "image/png"),
            AlternativeText = "Actual caption",
            Text = "Actual OCR",
        });
        section.Elements.Add(nested);

        var result = CloudDocumentIngestionProjection.Project(document);
        Assert.Equal("Local", result.Sections[0].Elements[0].Text);
        var projected = Assert.IsType<IngestionDocumentSection>(result.Sections[0].Elements[1]);
        Assert.Equal(2, Assert.IsType<IngestionDocumentHeader>(projected.Elements[0]).Level);
        var outer = Assert.IsType<IngestionDocumentTable>(projected.Elements[1]);
        Assert.IsType<IngestionDocumentTable>(outer.Cells[0, 0]);
        Assert.Contains("Native HTML", outer.GetMarkdown());
        var image = Assert.IsType<IngestionDocumentImage>(projected.Elements[2]);
        Assert.Equal("Actual caption", image.AlternativeText);
        Assert.Equal("Actual OCR", image.Text);
        Assert.Equal(new byte[] { 1, 2 }, image.Content!.Value.ToArray());
        Assert.All(projected.Elements, element => Assert.Equal(4, element.PageNumber));
    }

    [Fact]
    public void Project_UnmeasuredHeadingRoleAndMarkdownRemainParagraphs()
    {
        var document = new ExtractionDocument("source");
        var section = new ExtractionDocumentSection();
        section.Elements.Add(new ExtractionDocumentTextElement(new TextContent("# Native heading"), "heading"));
        document.Sections.Add(section);
        var element = Assert.Single(CloudDocumentIngestionProjection.Project(document).Sections[0].Elements);
        Assert.IsType<IngestionDocumentParagraph>(element);
        Assert.Equal("# Native heading", element.GetMarkdown());
        Assert.Null(element.PageNumber);
    }

    [Fact]
    public async Task ReadAsync_FirstRowContentCellIsNotInventedHeaderAndBlankCellsRemainBlank()
    {
        var native = DocumentIntelligenceModelFactory.AnalyzeResult(
            pages: [DocumentIntelligenceModelFactory.DocumentPage(pageNumber: 1)],
            tables: [DocumentIntelligenceModelFactory.DocumentTable(rowCount: 1, columnCount: 2,
                boundingRegions: [Region(1)], cells:
                [DocumentIntelligenceModelFactory.DocumentTableCell(content: "Value", kind: DocumentTableCellKind.Content),
                 DocumentIntelligenceModelFactory.DocumentTableCell(columnIndex: 1, content: "")])]);
        using var extraction = new AzureDocumentIntelligenceExtractionClient(AzureSdk(native));
        using var input = new MemoryStream([1]);
        var document = await new AzureDocumentIntelligenceReader(extraction)
            .ReadAsync(input, "source", "application/pdf");
        var table = Assert.IsType<IngestionDocumentTable>(Assert.Single(document.Sections[0].Elements));
        Assert.Contains("<td>Value</td>", table.GetMarkdown());
        Assert.DoesNotContain("<th", table.GetMarkdown());
        Assert.Null(table.Cells[0, 1]);
    }

    [Fact]
    public async Task ReadAsync_OverlappingTableCellsRejectInvalidLayout()
    {
        var native = DocumentIntelligenceModelFactory.AnalyzeResult(
            pages: [DocumentIntelligenceModelFactory.DocumentPage(pageNumber: 1)],
            tables: [DocumentIntelligenceModelFactory.DocumentTable(rowCount: 1, columnCount: 2,
                boundingRegions: [Region(1)], cells:
                [DocumentIntelligenceModelFactory.DocumentTableCell(columnSpan: 2, content: "Merged"),
                 DocumentIntelligenceModelFactory.DocumentTableCell(columnIndex: 1, content: "Overlaps")])]);
        using var extraction = new AzureDocumentIntelligenceExtractionClient(AzureSdk(native));
        using var input = new MemoryStream([1]);
        var error = await Record.ExceptionAsync(() =>
            new AzureDocumentIntelligenceReader(extraction).ReadAsync(input, "source", "application/pdf"));
        Assert.NotNull(error);
    }

    private static DocumentParagraph Paragraph(string content, int offset, int length, ParagraphRole? role = null) =>
        DocumentIntelligenceModelFactory.DocumentParagraph(content: content, role: role,
            boundingRegions: [Region(1)], spans: [Span(offset, length)]);
}
