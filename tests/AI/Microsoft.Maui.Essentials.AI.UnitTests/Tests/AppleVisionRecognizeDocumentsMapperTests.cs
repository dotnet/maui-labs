using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Documents;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.UnitTests;

public class AppleVisionRecognizeDocumentsMapperTests
{
	private static ExtractionDocument Map(string snapshot, int pageNumber = 1)
	{
		using var json = JsonDocument.Parse(snapshot);
		var document = new ExtractionDocument("input");
		AppleVisionRecognizeDocumentsMapper.AddPage(document, json.RootElement, pageNumber);
		return document;
	}

	private static string Text(ExtractionDocumentElement element) => Assert.IsType<TextContent>(element.Content).Text;

	[Fact]
	public void AddPage_PreservesExactFramePolygonAndDetachedNativeDetailsWithoutMetadata()
	{
		var result = Map("""
			[{"uuid":"obs","confidence":0.91,"transcript":"Full native transcript","polygon":[0,0,1,0,1,1],"nodes":[
			{"path":"p","kind":"paragraph","text":"Text","confidence":0.81,
			 "polygon":[-0.010000000000001,0.123456789012345,1.01,0.2,0.9,0.4,0.1,0.4],
			 "recognitionLanguages":["en"],"detectedData":[{"type":"emailAddress","value":"x@example.com"}],
			 "words":[{"polygon":[0,0,1,0,1,1],"candidates":[{"text":"Text","confidence":0.7}]}]}]}]
			""", 7);
		var frame = Assert.Single(result.SourcePages);
		Assert.Equal("input/page/7", frame.Identifier);
		Assert.Equal(7, frame.PageNumber);
		Assert.Equal(new ExtractionDocumentPageDimensions(1, 1), frame.Dimensions);
		Assert.Equal(ExtractionDocumentCoordinateOrigin.BottomLeft, frame.CoordinateOrigin);
		Assert.Equal(ExtractionDocumentCoordinateUnit.Normalized, frame.CoordinateUnit);
		var section = Assert.Single(result.Sections);
		var paragraph = Assert.IsType<ExtractionDocumentTextElement>(Assert.Single(section.Elements));
		Assert.Null(paragraph.Role);
		var reference = Assert.Single(paragraph.Sources);
		Assert.Same(frame, reference.Page);
		Assert.Equal(-0.010000000000001, reference.Polygon[0].X);
		Assert.Equal(0.123456789012345, reference.Polygon[0].Y);
		Assert.Equal(1.01, reference.Polygon[1].X);
		var native = Assert.IsType<JsonElement>(reference.GetRawRepresentation());
		Assert.Equal(0.81, native.GetProperty("confidence").GetDouble());
		Assert.Equal("en", native.GetProperty("recognitionLanguages")[0].GetString());
		Assert.Equal("emailAddress", native.GetProperty("detectedData")[0].GetProperty("type").GetString());
		Assert.False(native.GetProperty("words")[0].TryGetProperty("confidence", out _));
		Assert.Equal(0.7, native.GetProperty("words")[0].GetProperty("candidates")[0].GetProperty("confidence").GetDouble());
		var observation = Assert.IsType<JsonElement>(section.Sources[0].GetRawRepresentation())[0];
		Assert.Equal("Full native transcript", observation.GetProperty("transcript").GetString());
		Assert.Equal(0.91, observation.GetProperty("confidence").GetDouble());
		Assert.IsType<JsonElement>(paragraph.Content!.RawRepresentation);
		Assert.Empty(result.Metadata);
		Assert.Null(paragraph.Content.AdditionalProperties);
		Assert.Null(section.Content);
	}

	[Fact]
	public void AddPage_RetainsUnrecognizedNativeLeafCasesAndFieldsAsStructuredJson()
	{
		var result = Map("""
			[{"nodes":[{"path":"p","kind":"paragraph","text":"Text","detectedData":[
			{"details":{"newNativeCase":{"_0":{"newField":"preserved","amount":12.5,"nested":[1,2]}}},
			 "utf16Location":0,"utf16Length":4}]}]}]
			""");
		var paragraph = Assert.Single(result.Sections[0].Elements);
		var raw = Assert.IsType<JsonElement>(paragraph.Content!.RawRepresentation);
		var detection = raw.GetProperty("detectedData")[0];
		var details = detection.GetProperty("details").GetProperty("newNativeCase").GetProperty("_0");
		Assert.Equal("preserved", details.GetProperty("newField").GetString());
		Assert.Equal(12.5, details.GetProperty("amount").GetDouble());
		Assert.Equal(2, details.GetProperty("nested")[1].GetInt32());
		Assert.Equal(4, detection.GetProperty("utf16Length").GetInt32());
		Assert.Equal("Text", Text(paragraph));
		Assert.Null(paragraph.Content.AdditionalProperties);
	}

	[Fact]
	public void AddPage_ListsAndBarcodesUseTextOnlyWithNativeSemanticsAvailableExplicitly()
	{
		var result = Map("""
			[{"uuid":"obs","transcript":"Full transcript still exists","nodes":[
			{"path":"l","kind":"list"},
			{"path":"i","parentPath":"l","kind":"listItem","text":"Apples","itemString":"Apples","markerString":"1.","markerType":"decimal"},
			{"path":"b","kind":"barcode","payloadString":"QR content","symbology":"qr","payloadDataBase64":"AQID","confidence":0.8}]}]
			""");
		var elements = result.Sections[0].Elements;
		Assert.Equal("1. Apples", Text(elements[0]));
		var barcode = Assert.IsType<ExtractionDocumentTextElement>(elements[1]);
		Assert.Equal("QR content", Text(barcode));
		Assert.Null(barcode.Role);
		var native = Assert.IsType<JsonElement>(barcode.Content!.RawRepresentation);
		Assert.Equal("qr", native.GetProperty("symbology").GetString());
		Assert.Equal("AQID", native.GetProperty("payloadDataBase64").GetString());
		Assert.Equal(0.8, native.GetProperty("confidence").GetDouble());
		Assert.Empty(result.EnumerateContent().OfType<ExtractionDocumentImageElement>());
	}

	[Fact]
	public void AddPage_EmptyCellsSpansNestedTablesAndMixedCellContentSurvive()
	{
		var result = Map("""
			[{"nodes":[
			{"path":"t","kind":"table"},
			{"path":"c","parentPath":"t","kind":"tableCell","text":"Own cell","rowIndex":0,"columnIndex":0,"rowSpan":2,"columnSpan":2},
			{"path":"nested","parentPath":"c","kind":"table"},
			{"path":"nc","parentPath":"nested","kind":"tableCell","text":"Nested","rowIndex":0,"columnIndex":0,"rowSpan":1,"columnSpan":1},
			{"path":"p","parentPath":"c","kind":"paragraph","text":"Second content"},
			{"path":"empty","parentPath":"t","kind":"tableCell","text":"","rowIndex":0,"columnIndex":2,"rowSpan":1,"columnSpan":1,
			 "polygon":[0.1,0.2,0.3,0.2,0.3,0.4,0.1,0.4]}]}]
			""");
		var table = Assert.IsType<ExtractionDocumentTableElement>(Assert.Single(result.Sections[0].Elements));
		Assert.Equal(2, table.Cells.RowCount);
		Assert.Equal(3, table.Cells.ColumnCount);
		Assert.Equal(2, table.Cells[0, 0]!.RowSpan);
		Assert.Equal(2, table.Cells[0, 0]!.ColumnSpan);
		Assert.Null(table.Cells[0, 1]);
		Assert.Null(table.Cells[1, 2]);
		var empty = Assert.IsType<ExtractionDocumentTableCell>(table.Cells[0, 2]);
		Assert.Null(empty.Content);
		Assert.Null(empty.Role);
		Assert.Equal(4, Assert.Single(empty.Sources).Polygon.Count);
		Assert.Equal("Own cell", Assert.IsType<JsonElement>(table.Cells[0, 0]!.Sources[0].GetRawRepresentation()).GetProperty("text").GetString());
		var mixed = Assert.IsType<ExtractionDocumentSection>(table.Cells[0, 0]!.Content);
		Assert.Null(mixed.Content);
		Assert.Equal(2, mixed.Elements.Count);
		Assert.Contains(mixed.Elements, element => element is ExtractionDocumentTableElement nested && Text(nested.Cells[0, 0]!.Content!) == "Nested");
		Assert.Contains(mixed.Elements, element => element is ExtractionDocumentTextElement && Text(element) == "Second content");
	}

	[Fact]
	public void AddPage_PreservesNativeOrderAndIndependentSourceFrames()
	{
		const string snapshot = """
			[{"uuid":"A","nodes":[{"path":"p","kind":"paragraph","text":"Right first"},{"path":"q","kind":"paragraph","text":"Left second"}]},
			 {"uuid":"B","nodes":[{"path":"p","kind":"paragraph","text":"Third"}]}]
			""";
		var result = Map(snapshot, 3);
		using var json = JsonDocument.Parse(snapshot);
		AppleVisionRecognizeDocumentsMapper.AddPage(result, json.RootElement, 1);
		Assert.Equal(["Right first", "Left second", "Third"], result.Sections[0].Elements.Select(Text));
		Assert.Equal([3, 1], result.SourcePages.Select(page => page.PageNumber));
		Assert.NotSame(result.SourcePages[0], result.SourcePages[1]);
	}

	[Fact]
	public void AddPage_TitleReplacementRetainsParagraphRawDetailsWithoutInventingHeadingLevel()
	{
		var result = Map("""
			[{"nodes":[
			{"path":"title","kind":"title","text":"Heading","polygon":[0,0,1,0,1,1,0,1]},
			{"path":"p","kind":"paragraph","text":"Heading","confidence":0.7,"polygon":[0,0,1,0,1,1,0,1],
			 "detectedData":[{"type":"native-detection","value":"actual"}]}]}]
			""");
		var title = Assert.IsType<ExtractionDocumentTextElement>(Assert.Single(result.Sections[0].Elements));
		Assert.Equal("heading", title.Role);
		Assert.Null(title.HeadingLevel);
		Assert.False(Assert.IsType<JsonElement>(title.Content!.RawRepresentation).TryGetProperty("confidence", out _));
		var projection = Assert.IsType<JsonElement>(title.Sources[1].GetRawRepresentation());
		Assert.Equal(0.7, projection.GetProperty("confidence").GetDouble());
		Assert.Equal("actual", projection.GetProperty("detectedData")[0].GetProperty("value").GetString());
	}

	[Fact]
	public void AddPage_ListReconciliationUsesGeometryAndKeepsIndependentParagraph()
	{
		var result = Map("""
			[{"nodes":[
			{"path":"list","kind":"list","polygon":[0,0,1,0,1,1,0,1]},
			{"path":"item","parentPath":"list","kind":"listItem","text":"Apples","itemString":"Apples","markerString":"1.",
			 "polygon":[0,0,1,0,1,1,0,1]},
			{"path":"p","kind":"paragraph","text":"1.Apples","polygon":[0,0,1,0,1,1,0,1]},
			{"path":"independent","kind":"paragraph","text":"1.Apples","polygon":[2,0,3,0,3,1,2,1]}]}]
			""");
		Assert.Equal(["1.Apples", "1.Apples"], result.Sections[0].Elements.Select(Text));
		Assert.Equal(2, result.Sections[0].Elements[0].Sources.Count);
	}

	[Fact]
	public void AddPage_RecursiveItemProjectionsRenderOnceWithoutRemovingDistinctNestedItems()
	{
		var result = Map("""
			[{"nodes":[
			{"path":"list","kind":"list"},
			{"path":"item","parentPath":"list","kind":"listItem","text":"Apples","itemString":"Apples","markerString":"1.",
			 "polygon":[0,0,1,0,1,1,0,1]},
			{"path":"p","parentPath":"item","kind":"paragraph","text":"1. Apples","polygon":[0,0,1,0,1,1,0,1]},
			{"path":"repeated","parentPath":"item","kind":"list"},
			{"path":"r1","parentPath":"repeated","kind":"listItem","text":"Apples","itemString":"Apples","markerString":"1.",
			 "polygon":[0,0,1,0,1,1,0,1]},
			{"path":"r2","parentPath":"repeated","kind":"listItem","text":"Apples","itemString":"Apples","markerString":"1.",
			 "polygon":[0,0,1,0,1,1,0,1]},
			{"path":"distinct","parentPath":"item","kind":"list"},
			{"path":"d","parentPath":"distinct","kind":"listItem","text":"Apples","itemString":"Apples","markerString":"1.",
			 "polygon":[2,0,3,0,3,1,2,1]}]}]
			""");
		Assert.Equal("1. Apples\n1. Apples", Text(Assert.Single(result.Sections[0].Elements)));
		Assert.Equal(4, Assert.IsType<JsonElement>(result.Sections[0].Sources[0].GetRawRepresentation())[0]
			.GetProperty("nodes").EnumerateArray().Count(node => node.GetProperty("kind").GetString() == "listItem"));
	}

	[Fact]
	public void AddPage_NestedListAssociationPreservesNativeMarkers()
	{
		var result = Map("""
			[{"nodes":[
			{"path":"list","kind":"list","polygon":[0,0,1,0,1,1,0,1]},
			{"path":"item","parentPath":"list","kind":"listItem","text":"Apples","itemString":"Apples","markerString":"1.",
			 "polygon":[0,0,1,0,1,1,0,1]},
			{"path":"p","kind":"paragraph","text":"1.Apples","polygon":[0,0,1,0,1,1,0,1]},
			{"path":"nested","parentPath":"item","kind":"list","polygon":[0,0,1,0,1,0.5,0,0.5]},
			{"path":"nestedItem","parentPath":"nested","kind":"listItem","text":"Pears","itemString":"Pears","markerString":"•",
			 "polygon":[0,0,1,0,1,0.5,0,0.5]},
			{"path":"nestedParagraph","parentPath":"item","kind":"paragraph","text":"•Pears","polygon":[0,0,1,0,1,0.5,0,0.5]}]}]
			""");
		Assert.Equal("1.Apples\n•Pears", Text(Assert.Single(result.Sections[0].Elements)));
	}

	[Theory]
	[InlineData("""[{"transcript":"Actual transcript","nodes":[]} ]""", "Actual transcript")]
	[InlineData("""[]""", "")]
	public void AddPage_TranscriptAndEmptyPagesNeverClaimFormattedPayload(string snapshot, string text)
	{
		var result = Map(snapshot);
		Assert.Single(result.SourcePages);
		Assert.Null(result.Content);
		Assert.Null(result.Sections[0].Content);
		if (text.Length > 0) Assert.Equal(text, Text(Assert.Single(result.Sections[0].Elements)));
		else Assert.Empty(result.Sections[0].Elements);
	}

	[Theory]
	[InlineData("""[{"nodes":[{"path":"p","kind":"paragraph","parentPath":"missing"}]}]""")]
	[InlineData("""[{"nodes":[{"path":"p","kind":"paragraph","parentPath":"p"}]}]""")]
	[InlineData("""[{"nodes":[{"path":"p","kind":"paragraph"},{"path":"p","kind":"paragraph"}]}]""")]
	[InlineData("""[{"nodes":[{"path":"p","kind":"paragraph","polygon":[0,1,2]}]}]""")]
	[InlineData("""[{"nodes":[{"path":"p","kind":"paragraph","polygon":[0,0,1e400,0,1,1]}]}]""")]
	[InlineData("""[{"nodes":[{"path":"p","kind":"paragraph","confidence":2}]}]""")]
	[InlineData("""[{"nodes":[{"path":"c","kind":"tableCell","rowIndex":0,"columnIndex":0,"rowSpan":1,"columnSpan":1}]}]""")]
	[InlineData("""[{"nodes":[{"path":"item","kind":"listItem"}]}]""")]
	[InlineData("""[{"nodes":{}}]""")]
	[InlineData("""{}""")]
	[InlineData("""[{"uuid":"a","nodes":[]},{"uuid":"a","nodes":[]}]""")]
	[InlineData("""[{"nodes":[{"path":"t","kind":"table"},{"path":"c","parentPath":"t","kind":"tableCell","rowIndex":0,"columnIndex":0,"rowSpan":1,"columnSpan":2},{"path":"d","parentPath":"t","kind":"tableCell","rowIndex":0,"columnIndex":1,"rowSpan":1,"columnSpan":1}]}]""")]
	public void AddPage_MalformedSnapshot_Throws(string snapshot) =>
		Assert.Throws<InvalidDataException>(() => Map(snapshot));

	[Theory]
	[InlineData(64, false)]
	[InlineData(65, true)]
	public void AddPage_EnforcesTraversalDepth(int depth, bool invalid)
	{
		var nodes = Enumerable.Range(0, depth + 1).Select(index => new
		{
			path = index.ToString(), parentPath = index == 0 ? null : (index - 1).ToString(), kind = "paragraph", text = "Text",
		});
		var snapshot = JsonSerializer.Serialize(new[] { new { nodes } });
		if (invalid) Assert.Throws<InvalidDataException>(() => Map(snapshot));
		else Assert.Single(Map(snapshot).Sections[0].Elements);
	}

	[Fact]
	public void AddPage_RejectsUnboundedNodeCounts()
	{
		var nodes = Enumerable.Range(0, 20_001).Select(index => new { path = index.ToString(), kind = "paragraph" });
		Assert.Throws<InvalidDataException>(() => Map(JsonSerializer.Serialize(new[] { new { nodes } })));
	}
}
