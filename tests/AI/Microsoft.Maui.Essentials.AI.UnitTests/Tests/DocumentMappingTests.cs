using Microsoft.Extensions.AI;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;
using Xunit;

namespace AIExtensions.Sample.ChatPlayground;

public sealed class DocumentMappingTests
{
    [Fact]
    public async Task ReadAsync_PreservesPageAndElementOrderAndKeepsTableParagraphsInsideCells()
    {
        var result = new ExtractionDocument("layout.pdf");
        var firstPage = new ExtractionDocumentSourcePage("first", 3);
        var secondPage = new ExtractionDocumentSourcePage("second", 1);
        result.SourcePages.Add(firstPage);
        result.SourcePages.Add(secondPage);
        var section = new ExtractionDocumentSection();
        section.Sources.Add(new(firstPage));
        var cell = new ExtractionDocumentSection();
        cell.Elements.Add(new ExtractionDocumentTextElement(new TextContent("A")));
        var table = new ExtractionDocumentTableElement(null, 1, 2);
        table.Cells[0, 0] = new ExtractionDocumentTableCell(cell);
        table.Cells[0, 1] = new ExtractionDocumentTableCell(new ExtractionDocumentTextElement(new TextContent("B")));
        section.Elements.AddRange([
            new ExtractionDocumentTextElement(new TextContent("Title"), "heading"),
            table,
            new ExtractionDocumentTextElement(new TextContent("After")),
        ]);
        result.Sections.Add(section);
        var emptySection = new ExtractionDocumentSection();
        emptySection.Sources.Add(new(secondPage));
        result.Sections.Add(emptySection);
        using var extraction = new StubClient(result);
        using var input = new MemoryStream([1]);
        var document = await new ExtractedDocumentReader(extraction)
            .ReadAsync(input, "layout.pdf", "application/pdf");
        Assert.Equal("layout.pdf", document.Identifier);
        Assert.Equal([3, 1], document.Sections.Select(section => section.PageNumber));
        var elements = document.Sections[0].Elements;
        Assert.Equal("Title", elements[0].Text);
        Assert.IsType<IngestionDocumentParagraph>(elements[0]);
        var projectedTable = Assert.IsType<IngestionDocumentTable>(elements[1]);
        Assert.Equal("A", Assert.Single(Assert.IsType<IngestionDocumentSection>(projectedTable.Cells[0, 0]).Elements).Text);
        Assert.Equal("B", projectedTable.Cells[0, 1]!.Text);
        Assert.Equal("After", elements[2].Text);
        Assert.Equal(3, elements.Count);
        Assert.Empty(document.Sections[1].Elements);
        Assert.True(input.CanRead);
    }

    [Fact]
    public async Task ReadAsync_RendersReportedCellKindsAndSpansWithoutInventingHeaders()
    {
        var result = new ExtractionDocument("table.pdf");
        var page = new ExtractionDocumentSourcePage("page", 1);
        result.SourcePages.Add(page);
        var section = new ExtractionDocumentSection();
        section.Sources.Add(new(page));
        var grid = new ExtractionDocumentTableElement(null, 3, 2);
        grid.Cells[0, 0] = new ExtractionDocumentTableCell(
            new ExtractionDocumentTextElement(new TextContent("Names & roles")), 1, 2, "columnHeader");
        grid.Cells[1, 0] = new ExtractionDocumentTableCell(
            new ExtractionDocumentTextElement(new TextContent("Team")), 2, 1, "rowHeader");
        grid.Cells[1, 1] = new ExtractionDocumentTableCell(
            new ExtractionDocumentTextElement(new TextContent("Alex")), 1, 1, "content");
        grid.Cells[2, 1] = new ExtractionDocumentTableCell(new ExtractionDocumentTextElement(new TextContent("Sam")));
        section.Elements.Add(grid);
        result.Sections.Add(section);
        using var extraction = new StubClient(result);
        using var input = new MemoryStream([1]);
        var document = await new ExtractedDocumentReader(extraction)
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

        var result = ExtractionDocumentIngestionMapper.Project(document);
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
        var element = Assert.Single(ExtractionDocumentIngestionMapper.Project(document).Sections[0].Elements);
        Assert.IsType<IngestionDocumentParagraph>(element);
        Assert.Equal("# Native heading", element.GetMarkdown());
        Assert.Null(element.PageNumber);
    }

    [Fact]
    public async Task ReadAsync_MixedCellContentUsesExplicitSourcesAndBorrowsClientInputAndNativeObjects()
    {
        using var native = new MemoryStream([9]);
        var result = new ExtractionDocument("source")
        {
            Content = new TextContent("Document local"),
            RawRepresentation = native,
        };
        var section = new ExtractionDocumentSection { Content = new TextContent("Section local") };
        section.Sources.Add(new(new("section", 4)));
        var mixed = new ExtractionDocumentSection { Content = new TextContent("Cell local") };
        mixed.Elements.Add(new ExtractionDocumentTextElement(new TextContent("<before &>")));
        var nested = new ExtractionDocumentTableElement(null, 1, 1);
        var nestedText = new ExtractionDocumentTextElement(new TextContent("Nested"));
        nestedText.Sources.Add(new(new("nested", 9)));
        nested.Cells[0, 0] = new ExtractionDocumentTableCell(nestedText);
        mixed.Elements.Add(nested);
        mixed.Elements.Add(new ExtractionDocumentTextElement(new TextContent("After")));
        var outer = new ExtractionDocumentTableElement(null, 1, 1);
        var cell = new ExtractionDocumentTableCell(mixed);
        cell.Sources.Add(new(new("cell", 7), rawRepresentation: native));
        outer.Cells[0, 0] = cell;
        section.Elements.Add(outer);
        result.Sections.Add(section);
        using var extraction = new StubClient(result);
        var reader = new ExtractedDocumentReader(extraction);
        using var input = new MemoryStream([1]);

        var document = await reader.ReadAsync(input, "source", "application/pdf");
        Assert.Equal("Document local", Assert.Single(document.Sections[0].Elements).Text);
        Assert.Null(document.Sections[0].PageNumber);
        Assert.Equal("Section local", document.Sections[1].Elements[0].Text);
        var table = Assert.IsType<IngestionDocumentTable>(document.Sections[1].Elements[1]);
        Assert.Equal(4, table.PageNumber);
        var projected = Assert.IsType<IngestionDocumentSection>(table.Cells[0, 0]);
        Assert.Equal(["Cell local", "<before &>", "Nested", "After"], projected.Elements.Select(element => element.Text));
        Assert.All(projected.Elements, element => Assert.Equal(7, element.PageNumber));
        var nestedTable = Assert.IsType<IngestionDocumentTable>(projected.Elements[2]);
        Assert.Equal(9, nestedTable.Cells[0, 0]!.PageNumber);
        Assert.Contains("Cell local&lt;before &amp;&gt;\n<table><tr><td>Nested</td></tr></table>\nAfter", table.GetMarkdown());
        Assert.Same(native, result.RawRepresentation);
        Assert.Same(native, cell.Sources[0].GetRawRepresentation());
        Assert.True(native.CanRead);
        Assert.True(input.CanRead);
        Assert.False(extraction.Disposed);
        await reader.ReadAsync(input, "source", "application/pdf");
        Assert.False(extraction.Disposed);
    }

    [Fact]
    public async Task ReadAsync_FirstRowContentCellIsNotInventedHeaderAndBlankCellsRemainBlank()
    {
        var result = new ExtractionDocument("source");
        var section = new ExtractionDocumentSection();
        var grid = new ExtractionDocumentTableElement(null, 1, 3);
        grid.Cells[0, 0] = new ExtractionDocumentTableCell(
            new ExtractionDocumentTextElement(new TextContent("Value")), 1, 1, "content");
        grid.Cells[0, 1] = new ExtractionDocumentTableCell(new ExtractionDocumentTextElement(new TextContent("")));
        grid.Cells[0, 2] = new ExtractionDocumentTableCell();
        section.Elements.Add(grid);
        result.Sections.Add(section);
        using var extraction = new StubClient(result);
        using var input = new MemoryStream([1]);
        var document = await new ExtractedDocumentReader(extraction)
            .ReadAsync(input, "source", "application/pdf");
        var table = Assert.IsType<IngestionDocumentTable>(Assert.Single(document.Sections[0].Elements));
        Assert.Contains("<td>Value</td>", table.GetMarkdown());
        Assert.DoesNotContain("<th", table.GetMarkdown());
        Assert.Null(table.Cells[0, 1]);
        Assert.Null(table.Cells[0, 2]);
        Assert.Equal("<table><tr><td>Value</td><td></td><td></td></tr></table>", table.GetMarkdown());
    }

    [Fact]
    public void TableCells_OverlappingSpansRejectInvalidLayout()
    {
        var table = new ExtractionDocumentTableElement(null, 1, 2);
        table.Cells[0, 0] = new ExtractionDocumentTableCell(
            new ExtractionDocumentTextElement(new TextContent("Merged")), 1, 2);
        var error = Record.Exception(() => table.Cells[0, 1] =
            new ExtractionDocumentTableCell(new ExtractionDocumentTextElement(new TextContent("Overlaps"))));
        Assert.NotNull(error);
    }

    private sealed class StubClient(ExtractionDocument result) : DocumentExtractionClient
    {
        public bool Disposed { get; private set; }

        public override Task<ExtractionDocument> ExtractAsync(
            Stream document, string identifier, string mediaType, DocumentExtractionOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(result);
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
