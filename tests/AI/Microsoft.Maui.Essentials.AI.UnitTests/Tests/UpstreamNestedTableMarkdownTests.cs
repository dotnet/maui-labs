using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.UnitTests;

public class UpstreamNestedTableMarkdownTests
{
    [Fact]
    public void Finalization_PreservesNestedTableButCurrentMergedHtmlProjectionEscapesItsMarkup()
    {
        var nested = new DocumentExtractionTable("inner", 0, "", 1, 1,
            [new DocumentExtractionTableCell(0, 0,
                new DocumentExtractionText("leaf", 0, DocumentTextKind.Paragraph, "Value", "Value"))]);
        var outer = new DocumentExtractionTable("outer", 0, "", 1, 2,
            [new DocumentExtractionTableCell(0, 0, nested, columnSpan: 2)]);
        using var result = new[] { new DocumentExtractionPage(1, [outer]) }
            .ToDocumentExtractionResult("nested-table");
        var table = Assert.IsType<DocumentTableElement>(Assert.Single(Assert.Single(result.Document.Sections).Elements));
        var child = Assert.IsType<DocumentTableElement>(table.Cells[0, 0]!.Content);
        Assert.Equal("Value", Assert.IsType<DocumentTextElement>(child.Cells[0, 0]!.Content).Text);
        Assert.True(table.Cells.IsCovered(0, 1));

        // Upstream limitation: HTML encoding the complete child projection flattens its table markup.
        var markdown = result.Document.ToMarkdown();
        Assert.Contains("colspan", markdown);
        Assert.Contains("| Value |", markdown);
    }
}
