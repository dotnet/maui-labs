using Microsoft.Extensions.AI;
using Microsoft.Extensions.Documents;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.UnitTests;

public class UpstreamNestedTableMarkdownTests
{
    [Fact]
    public void Exchange_PreservesNestedAndMixedCellContentWithoutStructuralRenderer()
    {
        var nested = new ExtractionDocumentTableElement(null, 1, 1);
        nested.Cells[0, 0] = new(new ExtractionDocumentTextElement(new TextContent("Value")));
        var mixed = new ExtractionDocumentSection();
        mixed.Elements.AddRange([nested, new ExtractionDocumentImageElement(new DataContent(new byte[] { 1 }, "image/png"))]);
        var outer = new ExtractionDocumentTableElement(null, 1, 2);
        outer.Cells[0, 0] = new(mixed, columnSpan: 2);
        Assert.Same(nested, Assert.IsType<ExtractionDocumentSection>(outer.Cells[0, 0]!.Content).Elements[0]);
        Assert.Equal("Value", Assert.IsType<TextContent>(nested.Cells[0, 0]!.Content!.Content).Text);
        Assert.Null(outer.Cells[0, 1]);
        Assert.Null(mixed.Content);
        Assert.DoesNotContain(typeof(ExtractionDocumentTableElement).GetMethods(), method => method.Name == "ToMarkdown");
    }
}
