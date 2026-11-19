using Microsoft.Extensions.AI;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.UnitTests;

public class DocumentExtractionContractTests
{
    [Fact]
    public void Contract_UsesMergedAbstractClientAndNonDisposableExchangeModel()
    {
        var extraction = typeof(DocumentExtractionClient).Assembly;
        Assert.True(typeof(DocumentExtractionClient).IsAbstract);
        Assert.True(typeof(IDisposable).IsAssignableFrom(typeof(DocumentExtractionClient)));
        Assert.False(typeof(IDisposable).IsAssignableFrom(typeof(ExtractionDocument)));
        Assert.NotSame(extraction, typeof(ExtractionDocument).Assembly);
        foreach (var name in new[] { "IDocumentExtractionClient", "DocumentExtractionPage",
            "DocumentExtractionResult", "DocumentExtractionDocumentBuilder", "DocumentExtractionUsage" })
            Assert.Null(extraction.GetType("Microsoft.Extensions.DocumentExtraction." + name));
        Assert.Equal(typeof(Task<ExtractionDocument>), typeof(DocumentExtractionClient).GetMethod("ExtractAsync")!.ReturnType);
        Assert.Null(typeof(DocumentExtractionOptions).GetProperty("CaptureRawOutput"));
        Assert.Null(typeof(ExtractionDocumentTextElement).GetProperty("Confidence"));
        Assert.Null(typeof(ExtractionDocumentElement).GetProperty("Metadata"));
        Assert.DoesNotContain(typeof(ExtractionDocument).GetMethods(), method => method.Name.Contains("Markdown", StringComparison.Ordinal));
    }

    [Fact]
    public void Options_CloneSnapshotsSelectorAndDictionaryButBorrowsValues()
    {
        var borrowed = new object();
        var options = new DocumentExtractionOptions { ExtractorName = "analyzer" };
        options.AdditionalProperties["opaque"] = borrowed;
        var copy = options.Clone();
        options.ExtractorName = "changed";
        options.AdditionalProperties.Clear();
        Assert.Equal("analyzer", copy.ExtractorName);
        Assert.Same(borrowed, copy.AdditionalProperties["opaque"]);
        Assert.NotSame(options.AdditionalProperties, copy.AdditionalProperties);
        Assert.Null(new DocumentExtractionOptions().ExtractorName);
    }

    [Fact]
    public void Exchange_PreservesSourceVertexOrderSpansEmptyAnchorsAndCaptionlessBytes()
    {
        var frame = new ExtractionDocumentSourcePage("frame", 7, new(1, 1),
            ExtractionDocumentCoordinateUnit.Normalized, ExtractionDocumentCoordinateOrigin.BottomLeft);
        var raw = new object();
        var vertices = new[] { new ExtractionDocumentPoint(0.123456789012345, 0), new(1, 0), new(1, 1) };
        var reference = new ExtractionDocumentSourceReference(frame, vertices, raw);
        vertices[0] = new(0, 0);
        var table = new ExtractionDocumentTableElement(null, 2, 3);
        var merged = new ExtractionDocumentTableCell(2, 2, "actual-role");
        merged.Sources.Add(reference);
        table.Cells[0, 0] = merged;
        table.Cells[0, 2] = new ExtractionDocumentTableCell();
        var bytes = new byte[] { 1, 2, 3 };
        var image = new ExtractionDocumentImageElement(new DataContent(bytes, "image/png"));
        var document = new ExtractionDocument("document");
        var section = new ExtractionDocumentSection();
        section.Elements.AddRange([image, table]);
        document.Sections.Add(section);
        document.SourcePages.Add(frame);
        Assert.Equal(new byte[] { 1, 2, 3 }, Assert.IsType<DataContent>(image.Content).Data.ToArray());
        Assert.Null(image.AlternativeText);
        Assert.Null(image.Text);
        Assert.Null(section.Content);
        Assert.Equal(2, table.Cells[0, 0]!.RowSpan);
        Assert.Equal(2, table.Cells[0, 0]!.ColumnSpan);
        Assert.Equal("actual-role", table.Cells[0, 0]!.Role);
        Assert.Null(table.Cells[0, 0]!.Content);
        Assert.Null(table.Cells[1, 1]);
        Assert.NotNull(table.Cells[0, 2]);
        Assert.Null(table.Cells[1, 2]);
        Assert.Equal(0.123456789012345, reference.Polygon[0].X);
        Assert.Same(raw, reference.GetRawRepresentation());
        Assert.Equal([image, table], document.EnumerateContent());
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

    [Fact]
    public void NativeObjectsRemainBorrowedAndDoNotEnterSemanticPayloads()
    {
        using var native = new MemoryStream([1]);
        var document = new ExtractionDocument("document") { RawRepresentation = native };
        var section = new ExtractionDocumentSection();
        section.Sources.Add(new(new("page", 1), rawRepresentation: native));
        document.Sections.Add(section);
        Assert.Same(native, document.RawRepresentation);
        Assert.Same(native, section.Sources[0].GetRawRepresentation());
        Assert.Null(section.Content);
        Assert.True(native.CanRead);
        Assert.DoesNotContain("RawRepresentation", System.Text.Json.JsonSerializer.Serialize(document));
    }
}
