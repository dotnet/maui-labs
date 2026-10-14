using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.UnitTests;

public class DocumentExtractionContractTests
{
	[Fact]
	public void Contract_UsesCorrectedPublicExtractionAndSharedDocuments_NotDeletedTypes()
	{
		var extraction = typeof(IDocumentExtractionClient).Assembly;
		Assert.All(new[]
		{
			typeof(DocumentExtractionPage), typeof(DocumentExtractionText), typeof(DocumentExtractionImage),
			typeof(DocumentExtractionTable), typeof(DocumentExtractionTableCell), typeof(DocumentExtractionExtension),
			typeof(DocumentExtractionOptions), typeof(DocumentExtractionDocumentBuilder),
		}, type => { Assert.True(type.IsPublic); Assert.Same(extraction, type.Assembly); });
		foreach (var name in new[] { "DocumentBlock", "DocumentExtractionPageResult", "DocumentExtractionClientMetadata", "DocumentExtractionUsage" })
			Assert.Null(extraction.GetType("Microsoft.Extensions.DocumentExtraction." + name));
		Assert.NotSame(extraction, typeof(Document).Assembly);
		Assert.Equal(typeof(Document), typeof(DocumentExtractionResult).GetProperty("Document")!.PropertyType);
		Assert.Equal(["ExtractPagesAsync", "GetService"], typeof(IDocumentExtractionClient).GetMethods().Select(method => method.Name).Order());
	}

	[Fact]
	public void Options_CloneSnapshotsScalarsAndDictionary_LeavesOpaqueValuesBorrowed()
	{
		var borrowed = new object();
		var options = new DocumentExtractionOptions { ModelId = "model", CaptureRawOutput = false };
		options.AdditionalProperties["opaque"] = borrowed;
		var copy = options.Clone();
		options.ModelId = "changed";
		options.CaptureRawOutput = true;
		options.AdditionalProperties.Clear();
		Assert.Equal("model", copy.ModelId);
		Assert.False(copy.CaptureRawOutput);
		Assert.Same(borrowed, copy.AdditionalProperties["opaque"]);
		Assert.True(new DocumentExtractionOptions().CaptureRawOutput);
	}

	[Fact]
	public void Finalizer_PreservesAnnotationsRegionsIndependentEmptyCellsSpansAndImageWithoutMarkdown()
	{
		var source = new DocumentSourceOutput("output", captureDisabled: true);
		var frame = new DocumentSourcePage("frame", 7, new(1, 1), DocumentCoordinateUnit.Normalized, DocumentCoordinateOrigin.BottomLeft, source);
		var annotation = new DocumentAnnotation("native-cell", value: new[] { "native" }, role: "actual-role",
			regions: [new(frame, [new(0.123456789012345, 0), new(1, 0), new(1, 1)])], confidence: new(0.7, "actual", 0, 1));
		var table = new DocumentExtractionTable("table", 1, "", 2, 3,
			[new(0, 0, null, 2, 2, "actual-kind", [annotation]), new(0, 2, null, annotations: [annotation])],
			annotations: [annotation]);
		var image = new DocumentExtractionImage("image", 0, "", content: new byte[] { 1, 2, 3 }, mediaType: "image/png");
		using var result = new[] { new DocumentExtractionPage(7, [table, image], sourcePage: frame,
			annotations: [annotation], sourceOutputs: [source]) }.ToDocumentExtractionResult("document", false);
		var section = Assert.Single(result.Document.Sections);
		Assert.Same(annotation, Assert.Single(section.Annotations));
		var canonicalImage = Assert.IsType<DocumentImageElement>(section.Elements[0]);
		Assert.Equal(new byte[] { 1, 2, 3 }, canonicalImage.Content!.Value.ToArray());
		Assert.Equal("", canonicalImage.StoredMarkdown);
		Assert.Null(canonicalImage.Text);
		var canonicalTable = Assert.IsType<DocumentTableElement>(section.Elements[1]);
		Assert.Same(annotation, canonicalTable.Annotations[0]);
		Assert.Equal(2, canonicalTable.Cells[0, 0]!.RowSpan);
		Assert.Equal(2, canonicalTable.Cells[0, 0]!.ColumnSpan);
		Assert.Equal("actual-kind", canonicalTable.Cells[0, 0]!.Kind);
		Assert.Null(canonicalTable.Cells[0, 0]!.Content);
		Assert.Same(annotation, canonicalTable.Cells[0, 2]!.Annotations[0]);
		Assert.True(canonicalTable.Cells.IsCovered(1, 1));
		Assert.Null(canonicalTable.Cells[1, 2]);
		Assert.Equal(0.123456789012345, annotation.Regions[0].Polygon[0].X);
	}

	[Fact]
	public void Finalizer_SharedOperationBodyTransfersOnceAndFailedBuildClosesIt()
	{
		var stream = new CountingStream();
		var artifact = new DocumentArtifact("application/json", stream);
		var source = new DocumentSourceOutput("operation", rawContent: artifact);
		var pages = new[] { new DocumentExtractionPage(1, [], sourceOutputs: [source]),
			new DocumentExtractionPage(2, [], sourceOutputs: [source]) };
		using (var result = pages.ToDocumentExtractionResult("document"))
		{
			Assert.Single(result.Document.SourceOutputs);
			Assert.Single(result.Document.Artifacts);
			Assert.True(artifact.IsDocumentOwned);
			Assert.True(stream.CanRead);
		}
		Assert.Equal(1, stream.DisposeCount);
		var failed = new CountingStream();
		var badOutput = new DocumentSourceOutput("bad", rawContent: new("application/json", failed));
		Assert.Throws<ArgumentException>(() => new[]
		{
			new DocumentExtractionPage(1, [new DocumentExtractionText("duplicate", 0, DocumentTextKind.Paragraph, "one")], sourceOutputs: [badOutput]),
			new DocumentExtractionPage(2, [new DocumentExtractionText("duplicate", 0, DocumentTextKind.Paragraph, "two")]),
		}.ToDocumentExtractionResult("document"));
		Assert.Equal(1, failed.DisposeCount);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void Finalizer_RawOptOutRejectsTransitiveNativeObjectsOrBodies(bool body)
	{
		using var artifact = body ? new DocumentArtifact("application/json", new MemoryStream([1])) : null;
		var source = new DocumentSourceOutput("raw", rawRepresentation: body ? null : new object(), rawContent: artifact);
		var frame = new DocumentSourcePage("frame", 1, sourceOutput: source);
		var annotation = new DocumentAnnotation("cell", regions: [new(frame, [new(0, 0), new(1, 0), new(1, 1)])]);
		var page = new DocumentExtractionPage(1,
			[new DocumentExtractionTable("table", 0, "", 1, 1, [new(0, 0, null, annotations: [annotation])])]);
		Assert.Throws<ArgumentException>(() => new[] { page }.ToDocumentExtractionResult("input", false));
		if (body)
			Assert.True(artifact!.IsDisposed);
	}

	[Fact]
	public void Finalizer_StructuredExtensionValueIsTransferredDirectlyAndNotMetadata()
	{
		var value = new { NativeType = "barcode", Payload = "actual" };
		using var result = new[] { new DocumentExtractionPage(1,
			[new DocumentExtractionExtension("native", 0, "provider.barcode", "", value: value)]) }
			.ToDocumentExtractionResult("document");
		var element = Assert.IsType<DocumentExtensionElement>(Assert.Single(result.Document.Sections[0].Elements));
		Assert.Same(value, element.Value);
		Assert.False(element.HasMetadata);
	}

	private sealed class CountingStream : MemoryStream
	{
		public int DisposeCount { get; private set; }
		protected override void Dispose(bool disposing) { if (disposing) DisposeCount++; base.Dispose(disposing); }
	}
}
