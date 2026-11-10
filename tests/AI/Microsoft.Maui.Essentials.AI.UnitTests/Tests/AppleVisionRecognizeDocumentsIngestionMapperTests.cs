using System.Text.Json;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.Documents;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.UnitTests;

public class AppleVisionRecognizeDocumentsIngestionMapperTests
{
	[Fact]
	public void MapDocument_ProjectsFinalizedCanonicalPagesAndNeverFabricatesHeaderLevels()
	{
		using var json = JsonDocument.Parse("""
			[{"transcript":"Full transcript","nodes":[
			{"path":"title","kind":"title","text":"Heading"},{"path":"p","kind":"paragraph","text":"Body"}]}]
			""");
		var result = new ExtractionDocument("source");
		AppleVisionRecognizeDocumentsMapper.AddPage(result, json.RootElement, 3);
		var ingestion = AppleVisionRecognizeDocumentsIngestionMapper.MapDocument(result);
		Assert.Equal("source", ingestion.Identifier);
		var page = Assert.Single(ingestion.Sections);
		Assert.Equal(3, page.PageNumber);
		Assert.Contains(page.Elements, item => item.Text == "Heading");
		Assert.Contains(page.Elements, item => item.Text == "Body");
		Assert.Empty(page.Elements.OfType<IngestionDocumentHeader>());
		Assert.All(page.Elements, item => { Assert.False(item.HasMetadata); Assert.Equal(3, item.PageNumber); });
		Assert.Contains("Body", string.Join("\n", page.Elements.Select(item => item.GetMarkdown())));
	}

	[Fact]
	public void MapDocument_UsesFullTranscriptWhenNoSemanticElementsExist()
	{
		using var json = JsonDocument.Parse("""[{"transcript":"Actual transcript","nodes":[]} ]""");
		var result = new ExtractionDocument("input");
		AppleVisionRecognizeDocumentsMapper.AddPage(result, json.RootElement, 2);
		var paragraph = Assert.IsType<IngestionDocumentParagraph>(
			Assert.Single(Assert.Single(AppleVisionRecognizeDocumentsIngestionMapper.MapDocument(result).Sections).Elements));
		Assert.Equal("Actual transcript", paragraph.Text);
		Assert.Equal(2, paragraph.PageNumber);
	}

	[Fact]
	public void MapDocument_UsesCanonicalSpanMarkdownAndLogicalGridWithoutMetadata()
	{
		var source = new ExtractionDocument("input");
		var page = new ExtractionDocumentSection();
		page.Sources.Add(new(new ExtractionDocumentSourcePage("input/page/3", 3)));
		var grid = new ExtractionDocumentTableElement(null, 2, 3);
		grid.Cells[0, 0] = new(new ExtractionDocumentTextElement(new TextContent("Merged")), 1, 2);
		grid.Cells[0, 2] = new();
		grid.Cells[1, 2] = new(new ExtractionDocumentTextElement(new TextContent("8")));
		page.Elements.Add(grid);
		source.Sections.Add(page);
		var table = Assert.IsType<IngestionDocumentTable>(Assert.Single(
			Assert.Single(AppleVisionRecognizeDocumentsIngestionMapper.MapDocument(source).Sections).Elements));
		Assert.Equal("Merged", table.Cells[0, 0]!.Text);
		Assert.Null(table.Cells[0, 1]);
		Assert.Equal("8", table.Cells[1, 2]!.Text);
		Assert.Contains("colspan=\"2\"", table.GetMarkdown());
		Assert.False(table.HasMetadata);
	}

	[Theory]
	[InlineData(true, false)]
	[InlineData(true, true)]
	[InlineData(false, false)]
	[InlineData(false, true)]
	public void MapDocument_NestedAndMixedCellsRenderStructuralHtmlAndEncodeOnlyLeafText(bool merged, bool mixed)
	{
		var nested = new ExtractionDocumentTableElement(null, 1, 2);
		nested.Cells[0, 0] = new(new ExtractionDocumentTextElement(new TextContent("<inner & |>")));
		nested.Cells[0, 1] = new(new ExtractionDocumentTextElement(new TextContent("Other")));
		ExtractionDocumentElement content = nested;
		if (mixed)
		{
			var section = new ExtractionDocumentSection();
			section.Elements.Add(new ExtractionDocumentTextElement(new TextContent("<before & |>")));
			section.Elements.Add(nested);
			section.Elements.Add(new ExtractionDocumentTextElement(new TextContent("<after & |>")));
			content = section;
		}
		var outer = new ExtractionDocumentTableElement(null, 1, merged ? 2 : 1);
		outer.Cells[0, 0] = new(content, columnSpan: merged ? 2 : 1);
		var source = new ExtractionDocument("input");
		var page = new ExtractionDocumentSection();
		page.Elements.Add(outer);
		source.Sections.Add(page);

		var table = Assert.IsType<IngestionDocumentTable>(Assert.Single(
			Assert.Single(AppleVisionRecognizeDocumentsIngestionMapper.MapDocument(source).Sections).Elements));
		const string nestedHtml = """
			<table>
			<tr><td rowspan="1" colspan="1">&lt;inner &amp; |&gt;</td><td rowspan="1" colspan="1">Other</td></tr>
			</table>
			""";
		var expectedContent = mixed ? "&lt;before &amp; |&gt;\n" + nestedHtml + "\n&lt;after &amp; |&gt;" : nestedHtml;
		var expected = $"<table>\n<tr><td rowspan=\"1\" colspan=\"{(merged ? 2 : 1)}\">{expectedContent}</td></tr>\n</table>";
		Assert.Equal(expected, table.GetMarkdown());

		var html = new HtmlParser().ParseDocument(table.GetMarkdown());
		Assert.Equal(2, html.QuerySelectorAll("table").Length);
		var root = Assert.Single(html.Body!.Children);
		var outerCell = Assert.Single(Assert.Single(Assert.Single(root.Children).Children).Children);
		Assert.Equal((merged ? 2 : 1).ToString(), outerCell.GetAttribute("colspan"));
		var childTable = Assert.Single(outerCell.Children);
		Assert.Equal("TABLE", childTable.TagName);
		var childCells = childTable.QuerySelectorAll("td");
		Assert.Equal(2, childCells.Length);
		Assert.Equal("<inner & |>", childCells[0].TextContent);
		Assert.Equal("Other", childCells[1].TextContent);
		Assert.Equal(mixed ? "<before & |>\n\n<inner & |>Other\n\n<after & |>" : "\n<inner & |>Other\n", outerCell.TextContent);
	}

	[Fact]
	public void MapDocument_PlainTablePreservesPipeRendering()
	{
		var source = new ExtractionDocument("input");
		var page = new ExtractionDocumentSection();
		var grid = new ExtractionDocumentTableElement(null, 1, 2);
		grid.Cells[0, 0] = new(new ExtractionDocumentTextElement(new TextContent("A | B\nC")));
		grid.Cells[0, 1] = new(new ExtractionDocumentTextElement(new TextContent("D")));
		page.Elements.Add(grid);
		source.Sections.Add(page);
		var table = Assert.IsType<IngestionDocumentTable>(Assert.Single(
			Assert.Single(AppleVisionRecognizeDocumentsIngestionMapper.MapDocument(source).Sections).Elements));
		Assert.Equal("| A \\| B<br>C | D |\n| --- | --- |", table.GetMarkdown());
	}
}
