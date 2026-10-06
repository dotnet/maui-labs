using System.Text.Json;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.DocumentExtraction;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.UnitTests;

public class AppleVisionRecognizeDocumentsIngestionMapperTests
{
	private static IngestionDocumentSection MapPage(JsonElement snapshot, int pageNumber) =>
		AppleVisionRecognizeDocumentsIngestionMapper.MapPage(AppleVisionRecognizeDocumentsMapper.ToPage(snapshot, pageNumber));

	[Fact]
	public void MapPage_UsesActualPageTextWhenNoSupportedElementsAreAvailable()
	{
		var page = new DocumentPage(2, "Actual transcript");
		var paragraph = Assert.IsType<IngestionDocumentParagraph>(
			Assert.Single(AppleVisionRecognizeDocumentsIngestionMapper.MapPage(page).Elements));
		Assert.Equal("Actual transcript", paragraph.Text);
		Assert.Equal(2, paragraph.PageNumber);
		Assert.False(paragraph.HasMetadata);
	}

	[Fact]
	public void MapPage_PreservesHeadersColumnOrderAndPageNumbersWithoutMetadata()
	{
		using var json = JsonDocument.Parse("""
			[{"nodes":[
				{"path":"title","kind":"title","text":"Heading","polygon":[0,0.8,1,0.8,1,1,0,1]},
				{"path":"p1","kind":"paragraph","text":"Heading","polygon":[0,0.8,1,0.8,1,1,0,1]},
				{"path":"p2","kind":"paragraph","text":"Second column first"},
				{"path":"p3","kind":"paragraph","text":"First column second","confidence":0.99,
				 "detectedData":[{"kind":"email"}],"words":[{"text":"First"}]}
			]}]
			""");
		var section = MapPage(json.RootElement, 2);
		Assert.Equal(2, section.PageNumber);
		Assert.Collection(section.Elements,
			element => Assert.IsType<IngestionDocumentHeader>(element),
			element => Assert.Equal("Second column first", element.Text),
			element => Assert.Equal("First column second", element.Text));
		Assert.All(section.Elements, element =>
		{
			Assert.Equal(2, element.PageNumber);
			Assert.False(element.HasMetadata);
		});
	}

	[Fact]
	public void MapPage_MergedTablePreservesGridAndEscapesMarkdown()
	{
		using var json = JsonDocument.Parse("""
			[{"nodes":[
				{"path":"before","kind":"paragraph","text":"Before"},
				{"path":"table","kind":"table","polygon":[0,0.3,1,0.3,1,0.7,0,0.7]},
				{"path":"cell1","parentPath":"table","kind":"tableCell","text":"Merged | header",
				 "rowIndex":0,"columnIndex":0,"rowSpan":1,"columnSpan":2},
				{"path":"cell2","parentPath":"table","kind":"tableCell","text":"Count",
				 "rowIndex":0,"columnIndex":2,"rowSpan":1,"columnSpan":1},
				{"path":"cell3","parentPath":"table","kind":"tableCell","text":"8",
				 "rowIndex":2,"columnIndex":2,"rowSpan":1,"columnSpan":1},
				{"path":"duplicate","kind":"paragraph","text":"Merged | header","polygon":[0,0.5,0.6,0.5,0.6,0.6,0,0.6]},
				{"path":"after","kind":"paragraph","text":"After"}
			]}]
			""");
		var section = MapPage(json.RootElement, 3);
		Assert.Equal(3, section.Elements.Count);
		Assert.Equal("Before", section.Elements[0].Text);
		var table = Assert.IsType<IngestionDocumentTable>(section.Elements[1]);
		Assert.Equal(3, table.Cells.GetLength(0));
		Assert.Equal(3, table.Cells.GetLength(1));
		Assert.Equal("Merged | header", table.Cells[0, 0]?.Text);
		Assert.Null(table.Cells[0, 1]);
		Assert.Equal("Count", table.Cells[0, 2]?.Text);
		Assert.Equal("8", table.Cells[2, 2]?.Text);
		Assert.Contains("Merged \\| header", table.GetMarkdown());
		Assert.Equal("After", section.Elements[2].Text);
		Assert.False(table.HasMetadata);
		foreach (var cell in table.Cells)
			if (cell is not null)
			{
				Assert.Equal(3, cell.PageNumber);
				Assert.False(cell.HasMetadata);
			}
	}

	[Fact]
	public void MapPage_ListTextUsesActualParagraphsNotInventedListElements()
	{
		using var json = JsonDocument.Parse("""
			[{"nodes":[
				{"path":"list","kind":"list"},
				{"path":"item","parentPath":"list","kind":"listItem","text":"Apples","markerString":"-"},
				{"path":"text","kind":"paragraph","text":"- Apples"},
				{"path":"code","kind":"barcode","payloadString":"Code"}
			]}]
			""");
		var paragraph = Assert.IsType<IngestionDocumentParagraph>(Assert.Single(MapPage(json.RootElement, 1).Elements));
		Assert.Equal("- Apples", paragraph.Text);
		Assert.False(paragraph.HasMetadata);
	}

	[Theory]
	[InlineData("title", 0.699999999, false)]
	[InlineData("title", 0.7, true)]
	[InlineData("table", 0.699999999, false)]
	[InlineData("table", 0.7, true)]
	public void MapPage_UsesNativePrecisionForDuplicateRegionReplacement(string kind, double width, bool replaces)
	{
		var cell = kind == "table" ? """
			{"path":"cell","parentPath":"structured","kind":"tableCell","text":"Item",
			 "rowIndex":0,"columnIndex":0,"rowSpan":1,"columnSpan":1},
			""" : "";
		var extent = width.ToString(System.Globalization.CultureInfo.InvariantCulture);
		using var json = JsonDocument.Parse($$"""
			[{"nodes":[
				{"path":"structured","kind":"{{kind}}","text":"Item","polygon":[0,0,{{extent}},0,{{extent}},1,0,1]},
				{{cell}}
				{"path":"paragraph","kind":"paragraph","text":"Item","polygon":[0,0,1,0,1,1,0,1]}
			]}]
			""");
		Assert.Equal(replaces ? 1 : 2, MapPage(json.RootElement, 1).Elements.Count);
	}

	[Theory]
	[InlineData(null, "| Own \\| content |\n| --- |")]
	[InlineData("| Provider markdown |", "| Provider markdown |")]
	public void MapPage_TableUsesItsContentOrActualMarkdown(string? representation, string expected)
	{
		var cell = new DocumentTableCell(0, 0, "Own | content")
		{
			Elements = [new DocumentBlock("Nested text must not replace content")],
		};
		var page = new DocumentPage(2, "") { Elements = [new DocumentTable(1, 1, [cell], representation)] };
		var table = Assert.IsType<IngestionDocumentTable>(Assert.Single(AppleVisionRecognizeDocumentsIngestionMapper.MapPage(page).Elements));
		Assert.Equal(expected.Replace("\n", Environment.NewLine), table.GetMarkdown());
		Assert.Equal("Own | content", table.Cells[0, 0]?.Text);
		Assert.DoesNotContain("Nested", table.GetMarkdown());
	}
}
