using System.Text.Json;
using Microsoft.Extensions.DocumentExtraction;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.UnitTests;

public class AppleVisionRecognizeDocumentsMapperTests
{
	[Fact]
	public void ToPage_PreservesSupportedFieldsAndRawLifetimeWithoutPropertyBags()
	{
		DocumentPage page;
		using (var json = JsonDocument.Parse("""
			[{"uuid":"observation","confidence":0.9,"transcript":"Actual text","nodes":[
				{"path":"p","kind":"paragraph","text":"Actual text","confidence":0.8,
				 "polygon":[0.1,0.2,0.9,0.2,0.9,0.4,0.1,0.4],"recognitionLanguages":["en"],
				 "detectedData":[{"type":"emailAddress","value":"someone@example.com"}],
				 "words":[{"text":"Actual"}]},
				{"path":"code","kind":"barcode","payloadString":"payload","symbology":"qr"}]}]
			"""))
			page = AppleVisionRecognizeDocumentsMapper.ToPage(json.RootElement, 3);

		Assert.Equal(3, page.PageNumber);
		Assert.Equal("Actual text", page.Text);
		Assert.Equal(DocumentCoordinateUnit.Normalized, page.CoordinateUnit);
		Assert.Equal(DocumentCoordinateOrigin.BottomLeft, page.CoordinateOrigin);
		Assert.Equal(new DocumentPageDimensions(1, 1), page.Dimensions);
		var paragraph = Assert.IsType<DocumentBlock>(Assert.Single(page.Elements));
		Assert.Equal(DocumentBlockKind.Paragraph, paragraph.Kind);
		Assert.Equal(0.8, paragraph.Confidence);
		Assert.Equal(3, paragraph.BoundingRegion!.PageNumber);
		Assert.Equal(0.1f, paragraph.BoundingRegion.Polygon[0].X);
		Assert.Null(paragraph.AdditionalProperties);
		Assert.Null(page.AdditionalProperties);
		var raw = Assert.IsType<JsonElement>(page.RawRepresentation);
		Assert.Equal("qr", raw[0].GetProperty("nodes")[1].GetProperty("symbology").GetString());
		Assert.Equal("p", Assert.IsType<JsonElement>(paragraph.RawRepresentation).GetProperty("path").GetString());
	}

	[Fact]
	public void ToPage_TitleAndTablesReplaceOnlyOverlappingParagraphsInNativeOrder()
	{
		using var json = JsonDocument.Parse("""
			[{"nodes":[
				{"path":"title","kind":"title","text":"Heading","polygon":[0,0.8,1,0.8,1,1,0,1]},
				{"path":"titleText","kind":"paragraph","text":"Heading","polygon":[0,0.8,1,0.8,1,1,0,1]},
				{"path":"right","kind":"paragraph","text":"Right column first"},
				{"path":"left","kind":"paragraph","text":"Left column second"},
				{"path":"list","kind":"list"},
				{"path":"item","parentPath":"list","kind":"listItem","text":"Apples","markerString":"-"},
				{"path":"listText","kind":"paragraph","text":"- Apples"}
			]}]
			""");
		var page = AppleVisionRecognizeDocumentsMapper.ToPage(json.RootElement, 1);
		Assert.Equal(["Heading", "Right column first", "Left column second", "- Apples"],
			page.Elements.OfType<DocumentBlock>().Select(block => block.Text));
		Assert.Equal(DocumentBlockKind.Title, Assert.IsType<DocumentBlock>(page.Elements[0]).Kind);
		Assert.All(page.Elements.Skip(1), element =>
			Assert.Equal(DocumentBlockKind.Paragraph, Assert.IsType<DocumentBlock>(element).Kind));
	}

	[Fact]
	public void ToPage_TablePreservesLogicalSpansAndSupportedNestedContent()
	{
		using var json = JsonDocument.Parse("""
			[{"nodes":[
				{"path":"t","kind":"table"},
				{"path":"c","parentPath":"t","kind":"tableCell","text":"Own cell",
				 "rowIndex":1,"columnIndex":2,"rowSpan":2,"columnSpan":2},
				{"path":"p","parentPath":"c","kind":"paragraph","text":"Actual nested text"},
				{"path":"l","parentPath":"c","kind":"list"},
				{"path":"i","parentPath":"l","kind":"listItem","text":"Item","markerString":"1."}
			]}]
			""");
		var table = Assert.IsType<DocumentTable>(Assert.Single(AppleVisionRecognizeDocumentsMapper.ToPage(json.RootElement, 2).Elements));
		Assert.Equal(3, table.RowCount);
		Assert.Equal(4, table.ColumnCount);
		var cell = Assert.Single(table.Cells!);
		Assert.Equal(2, cell.RowSpan);
		Assert.Equal(2, cell.ColumnSpan);
		Assert.Equal("Own cell", cell.Content);
		Assert.Equal("Actual nested text", Assert.IsType<DocumentBlock>(Assert.Single(cell.Elements!)).Text);
		Assert.Null(cell.Kind);
		Assert.Null(cell.AdditionalProperties);
		Assert.Null(table.MarkdownRepresentation);
	}

	[Fact]
	public void ToPage_DoesNotInventFigureBytesRolesConfidenceOrCustomKinds()
	{
		using var json = JsonDocument.Parse("""
			[{"transcript":"List text","nodes":[
				{"path":"p","kind":"paragraph","text":"List text"},
				{"path":"l","kind":"list"},
				{"path":"i","parentPath":"l","kind":"listItem","text":"Item","markerString":"1."},
				{"path":"b","kind":"barcode","payloadString":"Code"},
				{"path":"unknown","kind":"futureAppleFeature","text":"future"}
			]}]
			""");
		var page = AppleVisionRecognizeDocumentsMapper.ToPage(json.RootElement, 1);
		var block = Assert.IsType<DocumentBlock>(Assert.Single(page.Elements));
		Assert.Null(block.Confidence);
		Assert.Null(block.BoundingRegion);
		Assert.Null(block.AdditionalProperties);
		Assert.Equal(DocumentBlockKind.Paragraph, block.Kind);
		Assert.Empty(page.Elements.OfType<DocumentImage>());
	}

	[Fact]
	public void ToPage_ObservationLocalPathsRemainIndependent()
	{
		using var json = JsonDocument.Parse("""
			[{"transcript":"A","nodes":[{"path":"p","kind":"paragraph","text":"One"}]},
			 {"transcript":"B","nodes":[{"path":"p","kind":"paragraph","text":"Two"}]}]
			""");
		var page = AppleVisionRecognizeDocumentsMapper.ToPage(json.RootElement, 1);
		Assert.Equal("A\n\nB", page.Text);
		Assert.Equal(["One", "Two"], page.Elements.OfType<DocumentBlock>().Select(block => block.Text));
	}

	[Fact]
	public void ToPage_EmptyNativeTableIsNotInventedAndGeometryIsNotClamped()
	{
		using var json = JsonDocument.Parse("""
			[{"transcript":"Actual text","nodes":[
				{"path":"emptyTable","kind":"table"},
				{"path":"p","kind":"paragraph","text":"Actual text","polygon":[-0.01,0,1.01,0,1.01,1,-0.01,1]}]}]
			""");
		var page = AppleVisionRecognizeDocumentsMapper.ToPage(json.RootElement, 1);
		var block = Assert.IsType<DocumentBlock>(Assert.Single(page.Elements));
		Assert.Equal(-0.01f, block.BoundingRegion!.Polygon[0].X);
		Assert.Equal(1.01f, block.BoundingRegion.Polygon[1].X);
	}

	[Theory]
	[InlineData("""[{"nodes":[{"path":"p","kind":"paragraph","parentPath":"missing"}]}]""")]
	[InlineData("""[{"nodes":[{"path":"p","kind":"paragraph","parentPath":"p"}]}]""")]
	[InlineData("""[{"nodes":[{"path":"p","kind":"paragraph"},{"path":"p","kind":"paragraph"}]}]""")]
	[InlineData("""[{"nodes":[{"path":"p","kind":"paragraph","polygon":[0,1,2]}]}]""")]
	[InlineData("""[{"nodes":[{"path":"p","kind":"paragraph","polygon":[0,0,1e400,0,1,1]}]}]""")]
	[InlineData("""[{"nodes":[{"path":"p","kind":"paragraph","confidence":2}]}]""")]
	[InlineData("""
		[{"nodes":[{"path":"t","kind":"table"},{"path":"c","parentPath":"t","kind":"tableCell",
		"rowIndex":2147483647,"columnIndex":0,"rowSpan":2147483647,"columnSpan":1}]}]
		""")]
	[InlineData("""[{"nodes":[{"path":"c","kind":"tableCell","rowIndex":0,"columnIndex":0,"rowSpan":1,"columnSpan":1}]}]""")]
	[InlineData("""[{"nodes":[{"path":"item","kind":"listItem"}]}]""")]
	[InlineData("""[{"nodes":{}}]""")]
	[InlineData("""{}""")]
	public void ToPage_MalformedSnapshot_Throws(string snapshot)
	{
		using var json = JsonDocument.Parse(snapshot);
		Assert.Throws<InvalidDataException>(() => AppleVisionRecognizeDocumentsMapper.ToPage(json.RootElement, 1));
	}

	[Theory]
	[InlineData(64, false)]
	[InlineData(65, true)]
	public void ToPage_EnforcesTraversalDepth(int depth, bool invalid)
	{
		var nodes = Enumerable.Range(0, depth + 1).Select(index => new
		{
			path = index.ToString(), parentPath = index == 0 ? null : (index - 1).ToString(), kind = "paragraph", text = "Text",
		});
		using var json = JsonDocument.Parse(JsonSerializer.Serialize(new[] { new { nodes } }));
		if (invalid)
			Assert.Throws<InvalidDataException>(() => AppleVisionRecognizeDocumentsMapper.ToPage(json.RootElement, 1));
		else
			Assert.Single(AppleVisionRecognizeDocumentsMapper.ToPage(json.RootElement, 1).Elements);
	}

	[Fact]
	public void ToPage_RejectsUnboundedNodeCounts()
	{
		var nodes = Enumerable.Range(0, 20_001).Select(index => new { path = index.ToString(), kind = "paragraph" });
		using var json = JsonDocument.Parse(JsonSerializer.Serialize(new[] { new { nodes } }));
		Assert.Throws<InvalidDataException>(() => AppleVisionRecognizeDocumentsMapper.ToPage(json.RootElement, 1));
	}
}
