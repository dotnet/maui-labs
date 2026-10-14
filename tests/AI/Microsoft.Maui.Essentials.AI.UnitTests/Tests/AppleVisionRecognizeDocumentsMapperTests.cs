using System.Text.Json;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.UnitTests;

public class AppleVisionRecognizeDocumentsMapperTests
{
	[Fact]
	public void ToPage_PreservesOnlySupportedFieldsWithoutOpaqueEscapes()
	{
		DocumentExtractionPage page;
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
		Assert.Null(page.Markdown);
		Assert.Empty(page.PageReferences);
		var paragraph = Assert.IsType<DocumentExtractionText>(Assert.Single(page.Observations));
		Assert.Equal(DocumentTextKind.Paragraph, paragraph.Kind);
		Assert.Equal("Actual text", paragraph.Text);
		Assert.Equal("Actual text", paragraph.Markdown);
		Assert.Empty(paragraph.PageReferences);
		Assert.Empty(paragraph.Evidence);
		Assert.Empty(paragraph.Artifacts);
		using var result = new[] { page }.ToDocumentExtractionResult("identity");
		Assert.False(result.Document.HasMetadata);
		Assert.Empty(result.Document.Evidence);
		Assert.Empty(result.Document.Artifacts);
		Assert.All(result.Document.EnumerateContent(), element =>
		{
			Assert.False(element.HasMetadata);
			Assert.Empty(element.Evidence);
			Assert.Empty(element.Artifacts);
		});
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
			page.Observations.OfType<DocumentExtractionText>().Select(block => block.Text));
		var header = Assert.IsType<DocumentExtractionText>(page.Observations[0]);
		Assert.Equal(DocumentTextKind.Header, header.Kind);
		Assert.Null(header.HeaderLevel);
		Assert.Equal([0, 1, 2, 3], page.Observations.Select(element => element.Order));
		Assert.All(page.Observations.Skip(1), element =>
			Assert.Equal(DocumentTextKind.Paragraph, Assert.IsType<DocumentExtractionText>(element).Kind));
	}

	[Fact]
	public void ToPage_TableKeepsAnchorTextAndDimensionsButDoesNotInventSpansOrNestedContent()
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
		var page = AppleVisionRecognizeDocumentsMapper.ToPage(json.RootElement, 2);
		var table = Assert.IsType<DocumentExtractionTable>(Assert.Single(page.Observations));
		Assert.Equal(3, table.RowCount);
		Assert.Equal(4, table.ColumnCount);
		var cell = Assert.Single(table.Cells);
		Assert.Equal(1, cell.Row);
		Assert.Equal(2, cell.Column);
		Assert.Equal("Own cell", Assert.IsType<DocumentExtractionText>(cell.Content).Text);
		Assert.Empty(cell.Content.Artifacts);
		Assert.Empty(cell.Content.Evidence);
		using var result = new[] { page }.ToDocumentExtractionResult("table");
		var mapped = Assert.IsType<DocumentTableElement>(Assert.Single(Assert.Single(result.Document.Sections).Elements));
		Assert.Equal("Own cell", mapped.Cells[1, 2]!.Content.Text);
		Assert.Null(mapped.Cells[1, 3]);
		Assert.Null(mapped.Cells[2, 2]);
		Assert.Null(mapped.Cells[2, 3]);
		Assert.DoesNotContain("Actual nested text", mapped.GetMarkdown());
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
		var block = Assert.IsType<DocumentExtractionText>(Assert.Single(page.Observations));
		Assert.Empty(block.Evidence);
		Assert.Empty(block.Artifacts);
		Assert.Equal(DocumentTextKind.Paragraph, block.Kind);
		Assert.Empty(page.Observations.OfType<DocumentExtractionImage>());
	}

	[Fact]
	public void ToPage_ObservationLocalPathsRemainIndependent()
	{
		using var json = JsonDocument.Parse("""
			[{"transcript":"A","nodes":[{"path":"p","kind":"paragraph","text":"One"}]},
			 {"transcript":"B","nodes":[{"path":"p","kind":"paragraph","text":"Two"}]}]
			""");
		var page = AppleVisionRecognizeDocumentsMapper.ToPage(json.RootElement, 1);
		Assert.Null(page.Markdown);
		Assert.Equal(["One", "Two"], page.Observations.OfType<DocumentExtractionText>().Select(block => block.Text));
		Assert.Equal(2, page.Observations.Select(block => block.ProviderId).Distinct().Count());
		using var result = new[] { page, AppleVisionRecognizeDocumentsMapper.ToPage(json.RootElement, 2) }
			.ToDocumentExtractionResult("multi-page");
		Assert.Equal(2, result.Document.Sections.Count);
	}

	[Fact]
	public void ToPage_EmptyNativeTableIsNotInventedAndGeometryRemainsInternal()
	{
		using var json = JsonDocument.Parse("""
			[{"transcript":"Actual text","nodes":[
				{"path":"emptyTable","kind":"table"},
				{"path":"p","kind":"paragraph","text":"Actual text","polygon":[-0.01,0,1.01,0,1.01,1,-0.01,1]}]}]
			""");
		var page = AppleVisionRecognizeDocumentsMapper.ToPage(json.RootElement, 1);
		var block = Assert.IsType<DocumentExtractionText>(Assert.Single(page.Observations));
		Assert.Equal("Actual text", block.Text);
		Assert.Empty(block.Evidence);
		Assert.Empty(block.PageReferences);
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
	[InlineData("""
		[{"nodes":[{"path":"t","kind":"table"},
		{"path":"c1","parentPath":"t","kind":"tableCell","rowIndex":0,"columnIndex":0,"rowSpan":1,"columnSpan":2},
		{"path":"c2","parentPath":"t","kind":"tableCell","rowIndex":0,"columnIndex":1,"rowSpan":1,"columnSpan":1}]}]
		""")]
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
			Assert.Single(AppleVisionRecognizeDocumentsMapper.ToPage(json.RootElement, 1).Observations);
	}

	[Fact]
	public void ToPage_RejectsUnboundedNodeCounts()
	{
		var nodes = Enumerable.Range(0, 20_001).Select(index => new { path = index.ToString(), kind = "paragraph" });
		using var json = JsonDocument.Parse(JsonSerializer.Serialize(new[] { new { nodes } }));
		Assert.Throws<InvalidDataException>(() => AppleVisionRecognizeDocumentsMapper.ToPage(json.RootElement, 1));
	}

	[Fact]
	public void ToPage_EmptyCellTextIsNotReplacedWithNestedTextOrPlaceholder()
	{
		using var json = JsonDocument.Parse("""
			[{"nodes":[{"path":"t","kind":"table"},
			{"path":"c","parentPath":"t","kind":"tableCell","text":"","rowIndex":0,"columnIndex":0,"rowSpan":1,"columnSpan":1},
			{"path":"p","parentPath":"c","kind":"paragraph","text":"Nested text"}]}]
			""");
		var page = AppleVisionRecognizeDocumentsMapper.ToPage(json.RootElement, 1);
		var table = Assert.IsType<DocumentExtractionTable>(Assert.Single(page.Observations));
		Assert.Empty(table.Cells);
		Assert.DoesNotContain("Nested text", table.Markdown);
		using var result = new[] { page }.ToDocumentExtractionResult("identity");
		var mapped = Assert.IsType<DocumentTableElement>(Assert.Single(result.Document.Sections[0].Elements));
		Assert.Null(mapped.Cells[0, 0]);
	}
}
