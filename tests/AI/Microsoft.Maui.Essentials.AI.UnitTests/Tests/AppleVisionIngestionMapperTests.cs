using System.Text.Json;
using Microsoft.Extensions.DataIngestion;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.UnitTests;

public class AppleVisionIngestionMapperTests
{
	private static IngestionDocumentSection MapPage(JsonElement snapshot, int pageNumber) =>
		AppleVisionIngestionMapper.MapPage(AppleVisionDocumentMapper.ToPage(snapshot, pageNumber));

	[Fact]
	public void MapPage_FullSnapshot_PreservesParagraphOrderAndOmitsAdvancedFields()
	{
		using var json = JsonDocument.Parse("""
			[{"nodes":[
				{"path":"title","kind":"title","text":"Heading","polygon":[0,0.8,1,0.8,1,1,0,1]},
				{"path":"p1","kind":"paragraph","text":"Heading","polygon":[0,0.8,1,0.8,1,1,0,1]},
				{"path":"p2","kind":"paragraph","text":"Second column first","polygon":[0.6,0.5,1,0.5,1,0.7,0.6,0.7]},
				{"path":"p3","kind":"paragraph","text":"First column second","polygon":[0,0.5,0.4,0.5,0.4,0.7,0,0.7],
				 "confidence":0.99,"detectedData":[{"kind":"email"}],"words":[{"text":"First"}]},
				{"path":"barcode","kind":"barcode","payloadString":"not ingestion text","confidence":1}
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
	public void MapPage_MergedTable_UsesLogicalGridAndSuppressesDuplicateParagraphs()
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
				{"path":"nested","parentPath":"cell1","kind":"paragraph","text":"Merged | header"},
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
		Assert.Equal(3, table.PageNumber);
		foreach (var cell in table.Cells)
			if (cell is not null)
			{
				Assert.Equal(3, cell.PageNumber);
				Assert.False(cell.HasMetadata);
			}
	}

	[Fact]
	public void MapPage_List_FlattensItemsWithoutRepeatingTheirParagraphs()
	{
		using var json = JsonDocument.Parse("""
			[{"nodes":[
				{"path":"list","kind":"list"},
				{"path":"item","parentPath":"list","kind":"listItem","text":"Apples","polygon":[0,0.5,1,0.5,1,0.6,0,0.6]},
				{"path":"self","parentPath":"item","kind":"paragraph","text":"Apples"},
				{"path":"nested","parentPath":"item","kind":"list"},
				{"path":"nestedItem","parentPath":"nested","kind":"listItem","text":"Coffee"},
				{"path":"p","kind":"paragraph","text":"Apples","polygon":[0,0.5,1,0.5,1,0.6,0,0.6]}
			]}]
			""");
		var section = MapPage(json.RootElement, 1);
		Assert.Equal(["Apples", "Coffee"], section.Elements.Select(element => element.Text));
		Assert.All(section.Elements, element => Assert.IsType<IngestionDocumentParagraph>(element));
	}

	[Theory]
	[InlineData(-1, 0, 1, 1)]
	[InlineData(0, 0, 0, 1)]
	[InlineData(0, 0, 1, 0)]
	[InlineData(20_000, 20_000, 1, 1)]
	public void MapPage_InvalidTableRange_Throws(int row, int column, int rowSpan, int columnSpan)
	{
		using var json = JsonDocument.Parse($$"""
			[{"nodes":[{"path":"t","kind":"table"},
			{"path":"c","kind":"tableCell","parentPath":"t","text":"bad",
			 "rowIndex":{{row}},"columnIndex":{{column}},"rowSpan":{{rowSpan}},"columnSpan":{{columnSpan}}}]}]
			""");
		Assert.Throws<InvalidDataException>(() => MapPage(json.RootElement, 1));
	}

	[Fact]
	public void MapPage_OverlappingTableCells_Throws()
	{
		using var json = JsonDocument.Parse("""
			[{"nodes":[
				{"path":"table","kind":"table"},
				{"path":"merged","kind":"tableCell","parentPath":"table","text":"Merged",
				 "rowIndex":0,"columnIndex":0,"rowSpan":1,"columnSpan":2},
				{"path":"covered","kind":"tableCell","parentPath":"table","text":"Covered",
				 "rowIndex":0,"columnIndex":1,"rowSpan":1,"columnSpan":1}
			]}]
			""");
		Assert.Throws<InvalidDataException>(() => MapPage(json.RootElement, 1));
	}

	[Fact]
	public void MapPage_CyclicListParents_ThrowsInsteadOfRecursing()
	{
		using var json = JsonDocument.Parse("""
			[{"nodes":[
				{"path":"list","parentPath":"item","kind":"list"},
				{"path":"item","parentPath":"list","kind":"listItem","text":"Apples"}
			]}]
			""");
		Assert.Throws<InvalidDataException>(() => MapPage(json.RootElement, 1));
	}

	[Theory]
	[InlineData("title", 0.69, false)]
	[InlineData("title", 0.7, true)]
	[InlineData("table", 0.69, false)]
	[InlineData("table", 0.7, true)]
	[InlineData("listItem", 0.54, false)]
	[InlineData("listItem", 0.55, true)]
	public void MapPage_OverlapThresholds_PreserveStructuredReplacement(string kind, double width, bool replaces)
	{
		var parent = kind == "listItem" ? """{"path":"list","kind":"list"},""" : "";
		var parentPath = kind == "listItem" ? ""","parentPath":"list" """ : "";
		var cell = kind == "table" ? """
			{"path":"cell","parentPath":"structured","kind":"tableCell","text":"Item",
			 "rowIndex":0,"columnIndex":0,"rowSpan":1,"columnSpan":1},
			""" : "";
		var paragraphText = kind == "listItem" ? "ITEM with extra text" : "Item";
		var extent = width.ToString(System.Globalization.CultureInfo.InvariantCulture);
		using var json = JsonDocument.Parse($$"""
			[{"nodes":[{{parent}}
				{"path":"structured","kind":"{{kind}}","text":"Item"{{parentPath}},
				 "polygon":[0,0,{{extent}},0,{{extent}},1,0,1]},
				{{cell}}
				{"path":"paragraph","kind":"paragraph","text":"{{paragraphText}}","polygon":[0,0,1,0,1,1,0,1]}
			]}]
			""");
		var section = MapPage(json.RootElement, 1);
		Assert.Equal(replaces ? 1 : 2, section.Elements.Count);
		if (!replaces)
			Assert.IsType<IngestionDocumentParagraph>(section.Elements[0]);
		switch (kind)
		{
			case "title": Assert.IsType<IngestionDocumentHeader>(section.Elements[^1]); break;
			case "table": Assert.IsType<IngestionDocumentTable>(section.Elements[^1]); break;
			default: Assert.Equal("Item", section.Elements[^1].Text); break;
		}
	}

	[Fact]
	public void MapPage_TitleReplacement_RequiresEqualText()
	{
		using var json = JsonDocument.Parse("""
			[{"nodes":[
				{"path":"title","kind":"title","text":"ITEM","polygon":[0,0,1,0,1,1,0,1]},
				{"path":"p","kind":"paragraph","text":"Item","polygon":[0,0,1,0,1,1,0,1]}
			]}]
			""");
		var section = MapPage(json.RootElement, 1);
		Assert.Equal(["Item", "ITEM"], section.Elements.Select(element => element.Text));
		Assert.IsType<IngestionDocumentParagraph>(section.Elements[0]);
		Assert.IsType<IngestionDocumentHeader>(section.Elements[1]);
	}

	[Fact]
	public void MapPage_ListDuplicates_RequireBidirectionalOverlapAndAppendAfterParagraphs()
	{
		using var json = JsonDocument.Parse("""
			[{"nodes":[
				{"path":"list","kind":"list"},
				{"path":"large","parentPath":"list","kind":"listItem","text":"Same","polygon":[0,0,1,0,1,0.3,0,0.3]},
				{"path":"small","parentPath":"list","kind":"listItem","text":"Same","polygon":[0,0,0.5,0,0.5,0.3,0,0.3]},
				{"path":"duplicate","parentPath":"list","kind":"listItem","text":"Same","polygon":[0,0,1,0,1,0.3,0,0.3]},
				{"path":"near","parentPath":"list","kind":"listItem","text":"Same","polygon":[0,0,0.9,0,0.9,0.3,0,0.3]},
				{"path":"elsewhere","parentPath":"list","kind":"listItem","text":"Same","polygon":[0,0.5,1,0.5,1,0.8,0,0.8]},
				{"path":"p","kind":"paragraph","text":"Paragraph first"}
			]}]
			""");
		var rich = AppleVisionDocumentMapper.ToPage(json.RootElement, 1);
		Assert.Equal(7, Assert.Single(rich.Observations).Nodes.Count);
		Assert.Equal(["Paragraph first", "Same", "Same", "Same"],
			AppleVisionIngestionMapper.MapPage(rich).Elements.Select(element => element.Text));
	}
}
