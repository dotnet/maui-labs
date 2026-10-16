using System.Text.Json;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.UnitTests;

public class AppleVisionRecognizeDocumentsMapperTests
{
	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void ToPage_PreservesExactFramePolygonOrderAndNativeConfidence(bool capture)
	{
		DocumentExtractionPage page;
		using (var json = JsonDocument.Parse("""
			[{"uuid":"obs","confidence":0.91,"transcript":"Full native transcript","polygon":[0,0,1,0,1,1],"nodes":[
			{"path":"p","kind":"paragraph","text":"Text","confidence":0.81,
			 "polygon":[-0.010000000000001,0.123456789012345,1.01,0.2,0.9,0.4,0.1,0.4],
			 "recognitionLanguages":["en"],"detectedData":[{"type":"emailAddress","value":"x@example.com"}],
			 "words":[{"polygon":[0,0,1,0,1,1],"candidates":[{"text":"Text","confidence":0.7}]}]}]}]
			"""))
			page = AppleVisionRecognizeDocumentsMapper.ToPage(json.RootElement, 7, "input", capture);
		using var result = new[] { page }.ToDocumentExtractionResult("input", capture);
		var frame = Assert.Single(result.Document.SourcePages);
		Assert.Equal("input/page/7", frame.Identifier);
		Assert.Equal(7, frame.PageNumber);
		Assert.Equal(new DocumentPageDimensions(1, 1), frame.Dimensions);
		Assert.Equal(DocumentCoordinateOrigin.BottomLeft, frame.CoordinateOrigin);
		Assert.Equal(DocumentCoordinateUnit.Normalized, frame.CoordinateUnit);
		var paragraph = Assert.IsType<DocumentTextElement>(Assert.Single(Assert.Single(result.Document.Sections).Elements));
		var annotation = paragraph.Annotations[0];
		Assert.Equal(0.81, annotation.Confidence!.Value);
		Assert.Same(frame, Assert.Single(annotation.Regions).Page);
		Assert.Equal(-0.010000000000001, annotation.Regions[0].Polygon[0].X);
		Assert.Equal(0.123456789012345, annotation.Regions[0].Polygon[0].Y);
		Assert.Equal(1.01, annotation.Regions[0].Polygon[1].X);
		Assert.Null(paragraph.Annotations.Single(item => item.Kind == "apple.vision.word").Confidence);
		Assert.Equal("Full native transcript", result.Document.Sections[0].Annotations[0].Value);
		Assert.Equal(0.91, result.Document.Sections[0].Annotations[0].Confidence!.Value);
		Assert.Same(frame, result.Document.Sections[0].Annotations[0].Regions[0].Page);
		Assert.False(paragraph.HasMetadata);
		Assert.False(result.Document.HasMetadata);
		var output = Assert.Single(result.Document.SourceOutputs);
		Assert.Equal(capture ? DocumentRawContentStatus.Retained : DocumentRawContentStatus.Disabled, output.NativeStatus);
		Assert.Equal(capture ? DocumentRawContentStatus.Unavailable : DocumentRawContentStatus.Disabled, output.ContentStatus);
		Assert.Contains("native-bridge-snapshot", output.ProviderName);
		if (capture)
			Assert.Equal("obs", Assert.IsType<JsonElement>(output.GetRawRepresentation())[0].GetProperty("uuid").GetString());
		else
		{
			Assert.Null(output.GetRawRepresentation());
			Assert.Null(output.GetRawContent());
			Assert.All(paragraph.Annotations, item => Assert.Null(item.GetRawRepresentation()));
			Assert.All(page.Annotations, item => Assert.Null(item.GetRawRepresentation()));
		}
	}

	[Fact]
	public void ToPage_PreservesTranscriptListsBarcodesAndActualSemanticValuesWithoutParagraphs()
	{
		using var json = JsonDocument.Parse("""
			[{"uuid":"obs","transcript":"Full transcript still exists","nodes":[
			{"path":"l","kind":"list"},
			{"path":"i","parentPath":"l","kind":"listItem","text":"Apples","itemString":"Apples","markerString":"1.","markerType":"decimal"},
			{"path":"b","kind":"barcode","payloadString":"QR content","symbology":"qr","payloadDataBase64":"AQID","confidence":0.8}]}]
			""");
		var page = AppleVisionRecognizeDocumentsMapper.ToPage(json.RootElement, 2, "input", false);
		using var result = new[] { page }.ToDocumentExtractionResult("input", false);
		Assert.Equal("Full transcript still exists", result.Document.Sections[0].Annotations[0].Value);
		var list = Assert.IsType<DocumentExtensionElement>(result.Document.Sections[0].Elements[0]);
		Assert.Equal("apple.vision.list", list.Type);
		var items = Assert.IsType<Dictionary<string, JsonElement>[]>(list.Value);
		Assert.Equal("1.", Assert.Single(items)["markerString"].GetString());
		var barcode = Assert.IsType<DocumentExtensionElement>(result.Document.Sections[0].Elements[1]);
		Assert.Equal("QR content", barcode.Text);
		Assert.Equal("qr", Assert.IsType<Dictionary<string, JsonElement>>(barcode.Value)["symbology"].GetString());
		Assert.Equal(0.8, barcode.Annotations[0].Confidence!.Value);
		Assert.Empty(result.Document.EnumerateContent().OfType<DocumentImageElement>());
	}

	[Fact]
	public void ToPage_IndependentEmptyCellsSpansAndNestedTablesSurviveFinalization()
	{
		using var json = JsonDocument.Parse("""
			[{"nodes":[
			{"path":"t","kind":"table"},
			{"path":"c","parentPath":"t","kind":"tableCell","text":"Own cell","rowIndex":0,"columnIndex":0,"rowSpan":2,"columnSpan":2},
			{"path":"nested","parentPath":"c","kind":"table"},
			{"path":"nc","parentPath":"nested","kind":"tableCell","text":"Nested","rowIndex":0,"columnIndex":0,"rowSpan":1,"columnSpan":1},
			{"path":"empty","parentPath":"t","kind":"tableCell","text":"","rowIndex":0,"columnIndex":2,"rowSpan":1,"columnSpan":1,
			 "polygon":[0.1,0.2,0.3,0.2,0.3,0.4,0.1,0.4]}]}]
			""");
		using var result = new[] { AppleVisionRecognizeDocumentsMapper.ToPage(json.RootElement, 3, "input", false) }
			.ToDocumentExtractionResult("input", false);
		var table = Assert.IsType<DocumentTableElement>(Assert.Single(result.Document.Sections[0].Elements));
		Assert.Equal(2, table.Cells.RowCount);
		Assert.Equal(3, table.Cells.ColumnCount);
		Assert.Equal(2, table.Cells[0, 0]!.RowSpan);
		Assert.Equal(2, table.Cells[0, 0]!.ColumnSpan);
		Assert.True(table.Cells.IsCovered(0, 1));
		Assert.Null(table.Cells[0, 1]);
		Assert.Null(table.Cells[1, 2]);
		var empty = Assert.IsType<DocumentTableCell>(table.Cells[0, 2]);
		Assert.Null(empty.Content);
		Assert.Null(empty.Kind);
		Assert.Single(empty.Annotations[0].Regions);
		Assert.Equal("Own cell", table.Cells[0, 0]!.Annotations[0].Value);
		var nested = Assert.IsType<DocumentTableElement>(table.Cells[0, 0]!.Content);
		Assert.Equal("Nested", nested.Cells[0, 0]!.Content!.Text);
	}

	[Fact]
	public void ToPage_PreservesNativeParagraphOrderAndGlobalObservationIdentity()
	{
		using var json = JsonDocument.Parse("""
			[{"uuid":"A","nodes":[{"path":"p","kind":"paragraph","text":"Right first"},{"path":"q","kind":"paragraph","text":"Left second"}]},
			 {"uuid":"B","nodes":[{"path":"p","kind":"paragraph","text":"Third"}]}]
			""");
		var first = AppleVisionRecognizeDocumentsMapper.ToPage(json.RootElement, 1, "source");
		var second = AppleVisionRecognizeDocumentsMapper.ToPage(json.RootElement, 3, "source");
		Assert.Equal(["Right first", "Left second", "Third"], first.Observations.OfType<DocumentExtractionText>().Select(item => item.Text));
		Assert.Equal(6, first.Observations.Concat(second.Observations).Select(item => item.ProviderId).Distinct().Count());
		using var result = new[] { second, first }.ToDocumentExtractionResult("source");
		Assert.Equal([1, 3], result.Document.SourcePages.Select(page => page.PageNumber));
		Assert.Equal("source/page/3", result.Document.SourcePages[1].Identifier);
	}

	[Fact]
	public void ToPage_StructuredReplacementRetainsActualParagraphDetailsAsAnnotations()
	{
		using var json = JsonDocument.Parse("""
			[{"nodes":[
			{"path":"title","kind":"title","text":"Heading","polygon":[0,0,1,0,1,1,0,1]},
			{"path":"p","kind":"paragraph","text":"Heading","confidence":0.7,"polygon":[0,0,1,0,1,1,0,1],
			 "detectedData":[{"type":"native-detection","value":"actual"}]}]}]
			""");
		var page = AppleVisionRecognizeDocumentsMapper.ToPage(json.RootElement, 1, "input", false);
		var title = Assert.IsType<DocumentExtractionText>(Assert.Single(page.Observations));
		Assert.Null(title.Annotations[0].Confidence);
		Assert.Equal(0.7, title.Annotations.Single(item => item.Kind == "apple.vision.paragraph").Confidence!.Value);
		Assert.Contains(title.Annotations, item => item.Kind == "apple.vision.detectedData");
		Assert.All(title.Annotations, item => Assert.Null(item.GetRawRepresentation()));
	}

	[Fact]
	public void ToPage_ListReconciliationUsesGeometryAndPreservesNativeMarkerOnceInFinalMarkdown()
	{
		using var json = JsonDocument.Parse("""
			[{"transcript":"1.Apples\n1.Apples","nodes":[
			{"path":"list","kind":"list","polygon":[0,0,1,0,1,1,0,1]},
			{"path":"item","parentPath":"list","kind":"listItem","text":"Apples","itemString":"Apples","markerString":"1.",
			 "polygon":[0,0,1,0,1,1,0,1]},
			{"path":"p","kind":"paragraph","text":"1.Apples","polygon":[0,0,1,0,1,1,0,1]},
			{"path":"independent","kind":"paragraph","text":"1.Apples","polygon":[2,0,3,0,3,1,2,1]}]}]
			""");
		using var result = new[] { AppleVisionRecognizeDocumentsMapper.ToPage(json.RootElement, 1, "source", false) }
			.ToDocumentExtractionResult("source", false);
		Assert.Null(result.Pages[0].Markdown);
		var section = result.Document.Sections[0];
		Assert.Equal(2, section.Elements.Count);
		var list = Assert.IsType<DocumentExtensionElement>(section.Elements[0]);
		Assert.Equal("apple.vision.list", list.Type);
		Assert.Equal("1.Apples", list.StoredMarkdown);
		Assert.Contains(list.Annotations, item => item.Kind == "apple.vision.paragraph" && Equals(item.Value, "1.Apples"));
		var rendered = result.Document.ToMarkdown();
		Assert.Equal(2, rendered.Split("1.Apples", StringSplitOptions.None).Length - 1);
		Assert.DoesNotContain("1. 1.", rendered);
		Assert.All(list.Annotations, item => Assert.Null(item.GetRawRepresentation()));
	}

	[Fact]
	public void ToPage_RecursiveItemProjectionsRenderOnceWithoutRemovingDistinctNestedItems()
	{
		using var json = JsonDocument.Parse("""
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
		using var result = new[] { AppleVisionRecognizeDocumentsMapper.ToPage(json.RootElement, 1, "source", false) }
			.ToDocumentExtractionResult("source", false);
		var list = Assert.IsType<DocumentExtensionElement>(Assert.Single(result.Document.Sections[0].Elements));
		Assert.Equal("1. Apples\n1. Apples", list.StoredMarkdown);
		Assert.Equal(4, list.Annotations.Count(annotation => annotation.Kind == "apple.vision.listItem"));
		Assert.Equal(3, list.Annotations.Count(annotation => annotation.Kind == "apple.vision.list"));
	}

	[Fact]
	public void ToPage_NestedListAssociationAndMarkersSurviveFinalCanonicalRendering()
	{
		using var json = JsonDocument.Parse("""
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
		using var result = new[] { AppleVisionRecognizeDocumentsMapper.ToPage(json.RootElement, 1, "source", false) }
			.ToDocumentExtractionResult("source", false);
		var list = Assert.IsType<DocumentExtensionElement>(Assert.Single(result.Document.Sections[0].Elements));
		Assert.Equal("1.Apples\n•Pears", result.Document.ToMarkdown());
		Assert.Equal(2, list.Annotations.Count(item => item.Kind == "apple.vision.list"));
		Assert.Equal(2, list.Annotations.Count(item => item.Kind == "apple.vision.listItem"));
	}

	[Theory]
	[InlineData("""[{"transcript":"Actual transcript","nodes":[]} ]""", "Actual transcript")]
	[InlineData("""[]""", "")]
	public void ToPage_TranscriptAndEmptyPagesNeverClaimProviderMarkdown(string snapshot, string rendered)
	{
		using var json = JsonDocument.Parse(snapshot);
		using var result = new[] { AppleVisionRecognizeDocumentsMapper.ToPage(json.RootElement, 1, "source", false) }
			.ToDocumentExtractionResult("source", false);
		Assert.Null(result.Pages[0].Markdown);
		Assert.Null(result.Document.StoredMarkdown);
		Assert.Equal(rendered, result.Document.ToMarkdown());
		Assert.Single(result.Document.SourcePages);
		if (rendered.Length > 0)
			Assert.Equal(rendered, result.Document.Sections[0].Annotations[0].Value);
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
	public void ToPage_MalformedSnapshot_Throws(string snapshot)
	{
		using var json = JsonDocument.Parse(snapshot);
		Assert.Throws<InvalidDataException>(() => AppleVisionRecognizeDocumentsMapper.ToPage(json.RootElement, 1, "input"));
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
			Assert.Throws<InvalidDataException>(() => AppleVisionRecognizeDocumentsMapper.ToPage(json.RootElement, 1, "input"));
		else
			Assert.Single(AppleVisionRecognizeDocumentsMapper.ToPage(json.RootElement, 1, "input").Observations);
	}

	[Fact]
	public void ToPage_RejectsUnboundedNodeCounts()
	{
		var nodes = Enumerable.Range(0, 20_001).Select(index => new { path = index.ToString(), kind = "paragraph" });
		using var json = JsonDocument.Parse(JsonSerializer.Serialize(new[] { new { nodes } }));
		Assert.Throws<InvalidDataException>(() => AppleVisionRecognizeDocumentsMapper.ToPage(json.RootElement, 1, "input"));
	}
}
