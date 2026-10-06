using System.Text.Json;
using Microsoft.Maui.Essentials.AI.Internal.DocumentExtraction;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.UnitTests;

public class AppleVisionDocumentMapperTests
{
	[Fact]
	public void ToPage_ReadingOrder_ReplacesDuplicateStructureAndRetainsFullHierarchy()
	{
		using var json = JsonDocument.Parse("""
			[{"nodes":[
				{"path":"title","kind":"title","text":"Heading","polygon":[0,0.8,1,0.8,1,1,0,1]},
				{"path":"titleParagraph","kind":"paragraph","text":"Heading","polygon":[0,0.8,1,0.8,1,1,0,1]},
				{"path":"right","kind":"paragraph","text":"Right column first","polygon":[0.6,0.5,1,0.5,1,0.7,0.6,0.7]},
				{"path":"left","kind":"paragraph","text":"Left column second","polygon":[0,0.5,0.4,0.5,0.4,0.7,0,0.7]},
				{"path":"listParagraph","kind":"paragraph","text":"Apples","polygon":[0,0.1,1,0.1,1,0.3,0,0.3]},
				{"path":"list","kind":"list"},
				{"path":"item","parentPath":"list","kind":"listItem","text":"Apples","markerString":"-",
				 "polygon":[0,0.1,1,0.1,1,0.3,0,0.3]},
				{"path":"self","parentPath":"item","kind":"paragraph","text":"Apples"},
				{"path":"barcode","kind":"barcode","payloadString":"Payload"}
			]}]
			""");
		var page = AppleVisionDocumentMapper.ToPage(json.RootElement, 1);
		Assert.Equal(["title", "right", "left", "item", "barcode"], page.Elements.Select(element => element.Path));
		var observation = Assert.Single(page.Observations);
		Assert.Equal(9, observation.Nodes.Count);
		Assert.Contains(observation.Elements, element => element.Path == "titleParagraph");
		var list = Assert.Single(observation.Elements, element => element.Path == "list");
		var item = Assert.Single(list.Elements);
		Assert.Equal("-", item.AdditionalProperties["markerString"]);
		Assert.Equal("self", Assert.Single(item.Elements).Path);
		Assert.Equal(["Heading", "Right column first", "Left column second", "Apples"],
			AppleVisionIngestionMapper.MapPage(page).Elements.Select(element => element.Text));
	}

	[Fact]
	public void ToPage_RichSnapshot_PreservesFieldsAndClonesRawData()
	{
		DocumentPage page;
		using (var json = JsonDocument.Parse("""
			[{"uuid":"first","confidence":0.9,"transcript":"Rich text","structureTruncated":true,
			  "projectedNodeCount":2,"maximumTraversalDepth":4,"repeatedContainerCount":1,
			  "firstRepeatedContainerPath":"repeat","firstRepeatedAncestorPath":"ancestor","futureDiagnostic":17,
			  "nodes":[
				{"path":"text","kind":"paragraph","text":"Rich text","confidence":0.8,
				 "polygon":[0.1,0.2,0.9,0.2,0.9,0.4,0.1,0.4],"textAlignment":"left",
				 "recognitionLanguages":["en","fr"],
				 "detectedData":[{"type":"emailAddress","value":"someone@example.com","utf16Location":0,"utf16Length":4}],
				 "candidates":[{"polygon":[0,0,1,0,1,1,0,1],"recognitionLanguages":["en"],
					"candidates":[{"text":"Rich text","confidence":0.8}]}],
				 "words":[{"polygon":[0,0,1,0,1,1,0,1],"recognitionLanguages":["fr"],
					"candidates":[{"text":"Rich","confidence":0.7}]}]},
				{"path":"code","kind":"barcode","payloadString":"payload","payloadDataBase64":"AQID",
				 "symbology":"qr","isGS1DataCarrier":true,"isColorInverted":false,"confidence":0.95,
				 "supplementalPayloadString":"extra","supplementalPayloadDataBase64":"BAU=",
				 "supplementalCompositeType":"linked","futureField":{"kept":true}}
			]}]
			"""))
			page = AppleVisionDocumentMapper.ToPage(json.RootElement, 3, 1600, 1200, 2);

		Assert.Equal("Rich text", page.Text);
		Assert.Equal(3, page.PageNumber);
		Assert.Equal(1, page.Dimensions.Width);
		Assert.Equal(1, page.Dimensions.Height);
		Assert.Equal(DocumentCoordinateUnit.Normalized, page.CoordinateUnit);
		Assert.Equal(DocumentCoordinateOrigin.BottomLeft, page.CoordinateOrigin);
		Assert.Equal(1600, page.AdditionalProperties["apple.sourcePixelWidth"]);
		Assert.Equal(1200, page.AdditionalProperties["apple.sourcePixelHeight"]);
		Assert.Equal(2, page.AdditionalProperties["apple.vision.revision"]);
		Assert.Equal(true, page.AdditionalProperties["apple.vision.structureTruncated"]);
		var observation = Assert.Single(page.Observations);
		Assert.Equal("first", observation.Id);
		Assert.Equal(0.9, observation.Confidence);
		Assert.Equal(4d, observation.AdditionalProperties["maximumTraversalDepth"]);
		Assert.Equal(1d, observation.AdditionalProperties["repeatedContainerCount"]);
		Assert.Equal("repeat", observation.AdditionalProperties["firstRepeatedContainerPath"]);
		Assert.Equal("ancestor", observation.AdditionalProperties["firstRepeatedAncestorPath"]);
		Assert.Equal(17d, observation.AdditionalProperties["futureDiagnostic"]);
		var text = Assert.IsType<DocumentBlock>(observation.Nodes[0]);
		Assert.Equal(0.8, text.Confidence);
		Assert.Equal(3, text.BoundingRegion?.PageNumber);
		Assert.Equal(0.1, text.BoundingRegion?.Polygon[0].X);
		Assert.Equal(0.2, text.BoundingRegion?.Polygon[0].Y);
		Assert.Equal("left", text.AdditionalProperties["textAlignment"]);
		var languages = Assert.IsType<JsonElement>(text.AdditionalProperties["recognitionLanguages"]);
		Assert.Equal(["en", "fr"], languages.EnumerateArray().Select(value => value.GetString()));
		var data = Assert.IsType<JsonElement>(text.AdditionalProperties["detectedData"]);
		Assert.Equal("someone@example.com", data[0].GetProperty("value").GetString());
		var candidates = Assert.IsType<JsonElement>(text.AdditionalProperties["candidates"]);
		Assert.Equal("Rich text", candidates[0].GetProperty("candidates")[0].GetProperty("text").GetString());
		var words = Assert.IsType<JsonElement>(text.AdditionalProperties["words"]);
		Assert.Equal(0.7, words[0].GetProperty("candidates")[0].GetProperty("confidence").GetDouble());
		var barcode = Assert.IsType<DocumentBlock>(observation.Nodes[1]);
		Assert.Equal(DocumentBlockKind.Barcode, barcode.Kind);
		Assert.Equal("payload", barcode.Text);
		Assert.Equal("AQID", barcode.AdditionalProperties["payloadDataBase64"]);
		Assert.Equal("qr", barcode.AdditionalProperties["symbology"]);
		Assert.Equal(true, barcode.AdditionalProperties["isGS1DataCarrier"]);
		Assert.Equal(false, barcode.AdditionalProperties["isColorInverted"]);
		Assert.Equal("extra", barcode.AdditionalProperties["supplementalPayloadString"]);
		Assert.Equal("BAU=", barcode.AdditionalProperties["supplementalPayloadDataBase64"]);
		Assert.Equal("linked", barcode.AdditionalProperties["supplementalCompositeType"]);
		Assert.True(Assert.IsType<JsonElement>(barcode.AdditionalProperties["futureField"]).GetProperty("kept").GetBoolean());
		Assert.Equal("first", Assert.IsType<JsonElement>(page.RawRepresentation)[0].GetProperty("uuid").GetString());
		Assert.Equal("first", Assert.IsType<JsonElement>(observation.RawRepresentation).GetProperty("uuid").GetString());
		Assert.Equal("text", Assert.IsType<JsonElement>(text.RawRepresentation).GetProperty("path").GetString());
		Assert.Single(AppleVisionIngestionMapper.MapPage(page).Elements);
	}

	[Fact]
	public void ToPage_NestedListsAndTableCells_RetainsContainersAndSpanContent()
	{
		using var json = JsonDocument.Parse("""
			[{"nodes":[
				{"path":"table","kind":"table"},
				{"path":"cell","parentPath":"table","kind":"tableCell","text":"Own cell text",
				 "rowIndex":1,"columnIndex":2,"rowSpan":2,"columnSpan":2},
				{"path":"list","parentPath":"cell","kind":"list","confidence":0.8},
				{"path":"item","parentPath":"list","kind":"listItem","text":"1. Item",
				 "itemString":"Item","markerString":"1.","markerType":"decimal"},
				{"path":"self","parentPath":"item","kind":"paragraph","text":"1. Item"},
				{"path":"nested","parentPath":"item","kind":"list"},
				{"path":"nestedItem","parentPath":"nested","kind":"listItem","text":"Child"}
			]}]
			""");
		var page = AppleVisionDocumentMapper.ToPage(json.RootElement, 2);
		var table = Assert.IsType<DocumentTable>(Assert.Single(page.Elements));
		Assert.Equal(3, table.RowCount);
		Assert.Equal(4, table.ColumnCount);
		var cell = Assert.Single(table.Cells);
		Assert.Equal(2, cell.RowSpan);
		Assert.Equal(2, cell.ColumnSpan);
		var list = Assert.IsType<DocumentBlock>(Assert.Single(cell.Elements));
		Assert.Equal(DocumentBlockKind.List, list.Kind);
		var item = Assert.IsType<DocumentBlock>(Assert.Single(list.Elements));
		Assert.Equal(DocumentBlockKind.ListItem, item.Kind);
		Assert.Equal("Item", item.AdditionalProperties["itemString"]);
		Assert.Equal("1.", item.AdditionalProperties["markerString"]);
		Assert.Equal("decimal", item.AdditionalProperties["markerType"]);
		Assert.Equal(2, item.Elements.Count);
		Assert.Equal("1. Item", item.Elements[0].Text);
		Assert.Equal("Child", Assert.Single(item.Elements[1].Elements).Text);
		Assert.Same(cell, list.Parent);
		var ingestionTable = Assert.IsType<Microsoft.Extensions.DataIngestion.IngestionDocumentTable>(
			Assert.Single(AppleVisionIngestionMapper.MapPage(page).Elements));
		Assert.Equal("Own cell text", ingestionTable.Cells[1, 2]?.Text);
		Assert.Null(ingestionTable.Cells[2, 3]);
		Assert.DoesNotContain("Child", ingestionTable.GetMarkdown());
	}

	[Fact]
	public void ToPage_DuplicateLocalPathsAcrossObservations_KeepsGroupsAndDoesNotDeduplicate()
	{
		using var json = JsonDocument.Parse("""
			[{"uuid":"first","transcript":"A","nodes":[
				{"path":"list","kind":"list"},
				{"path":"item","parentPath":"list","kind":"listItem","text":"Same","polygon":[0,0,1,0,1,1,0,1]},
				{"path":"p","kind":"paragraph","text":"Same","polygon":[0,0,1,0,1,1,0,1]}]},
			 {"uuid":"second","transcript":"B","nodes":[
				{"path":"list","kind":"list"},
				{"path":"item","parentPath":"list","kind":"listItem","text":"Same","polygon":[0,0,1,0,1,1,0,1]},
				{"path":"p","kind":"paragraph","text":"Same","polygon":[0,0,1,0,1,1,0,1]}]}]
			""");
		var page = AppleVisionDocumentMapper.ToPage(json.RootElement, 1);
		Assert.Equal("A\n\nB", page.Text);
		Assert.Equal(2, page.Observations.Count);
		Assert.NotSame(page.Observations[0].Nodes[0], page.Observations[1].Nodes[0]);
		Assert.Same(page.Observations[1].Nodes[0], page.Observations[1].Nodes[1].Parent);
		Assert.Equal(["Same", "Same"], AppleVisionIngestionMapper.MapPage(page).Elements.Select(value => value.Text));
	}

	[Theory]
	[InlineData("""[{"nodes":[{"path":"p","kind":"paragraph","parentPath":"missing"}]}]""")]
	[InlineData("""[{"nodes":[{"path":"p","kind":"paragraph","parentPath":"p"}]}]""")]
	[InlineData("""[{"nodes":[{"path":"p","kind":"paragraph"},{"path":"p","kind":"paragraph"}]}]""")]
	[InlineData("""[{"nodes":[{"path":"p","kind":"paragraph","polygon":[0,1,2]}]}]""")]
	[InlineData("""[{"nodes":[{"path":"p","kind":"paragraph","polygon":[0,1e400]}]}]""")]
	[InlineData("""
		[{"nodes":[{"path":"t","kind":"table"},{"path":"c","parentPath":"t","kind":"tableCell",
		"rowIndex":2147483647,"columnIndex":0,"rowSpan":2147483647,"columnSpan":1}]}]
		""")]
	[InlineData("""
		[{"nodes":[{"path":"t","kind":"table"},{"path":"c","parentPath":"t","kind":"tableCell",
		"rowIndex":0,"columnIndex":0,"rowSpan":200,"columnSpan":101}]}]
		""")]
	[InlineData("""[{"nodes":[{"path":"c","kind":"tableCell","rowIndex":0,"columnIndex":0,"rowSpan":1,"columnSpan":1}]}]""")]
	[InlineData("""[{"nodes":[{"path":"item","kind":"listItem"}]}]""")]
	[InlineData("""[{"nodes":{}}]""")]
	[InlineData("""{}""")]
	public void ToPage_MalformedSnapshot_ThrowsInvalidData(string snapshot)
	{
		using var json = JsonDocument.Parse(snapshot);
		Assert.Throws<InvalidDataException>(() => AppleVisionDocumentMapper.ToPage(json.RootElement, 1));
	}

	[Theory]
	[InlineData(64, false)]
	[InlineData(65, true)]
	public void ToPage_HierarchyDepth_EnforcesBound(int depth, bool invalid)
	{
		var nodes = Enumerable.Range(0, depth + 1).Select(index => new
		{
			path = index.ToString(),
			parentPath = index == 0 ? null : (index - 1).ToString(),
			kind = "paragraph",
		});
		using var json = JsonDocument.Parse(JsonSerializer.Serialize(new[] { new { nodes } }));
		if (invalid)
			Assert.Throws<InvalidDataException>(() => AppleVisionDocumentMapper.ToPage(json.RootElement, 1));
		else
			Assert.Equal(depth + 1, Assert.Single(AppleVisionDocumentMapper.ToPage(json.RootElement, 1).Observations).Nodes.Count);
	}

	[Fact]
	public void ToPage_TooManyNodes_Throws()
	{
		var nodes = Enumerable.Range(0, 20_001).Select(index => new { path = index.ToString(), kind = "paragraph" });
		using var json = JsonDocument.Parse(JsonSerializer.Serialize(new[] { new { nodes } }));
		Assert.Throws<InvalidDataException>(() => AppleVisionDocumentMapper.ToPage(json.RootElement, 1));
	}
}
