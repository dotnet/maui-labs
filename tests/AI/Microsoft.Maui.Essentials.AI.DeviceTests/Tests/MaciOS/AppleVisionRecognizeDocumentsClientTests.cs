#if IOS || MACCATALYST
using System.Text.Json;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;
using Microsoft.Maui.Storage;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

public class AppleVisionRecognizeDocumentsClientTests
{
	[AppleVisionRecognizeDocumentsFact]
	public async Task ExtractAsync_ImagePreservesCanonicalAnnotationsAndTruthfulNativeBridgeOutput()
	{
		using IDocumentExtractionClient client = new AppleVisionRecognizeDocumentsClient();
		Assert.Same(client, client.GetService(typeof(IDocumentExtractionClient)));
		using var image = await FileSystem.OpenAppPackageFileAsync("DocumentReader/headings.png");
		using var result = await client.ExtractAsync(image, "headings", "image/png");
		var page = Assert.Single(result.Pages);
		Assert.Null(page.Markdown);
		Assert.Contains("DOCUMENT EXTRACTION EVALUATION", result.Document.ToMarkdown(), StringComparison.OrdinalIgnoreCase);
		Assert.Equal(DocumentCoordinateUnit.Normalized, page.SourcePage!.CoordinateUnit);
		Assert.Equal(DocumentCoordinateOrigin.BottomLeft, page.SourcePage.CoordinateOrigin);
		Assert.Equal(new DocumentPageDimensions(1, 1), page.SourcePage.Dimensions);
		Assert.Contains(page.Observations, item => item is DocumentExtractionText { Kind: DocumentTextKind.Header });
		Assert.False(result.Document.HasMetadata);
		Assert.All(result.Document.EnumerateContent(), element => Assert.False(element.HasMetadata));
		var output = Assert.Single(result.Document.SourceOutputs);
		Assert.Equal(DocumentRawContentStatus.Retained, output.NativeStatus);
		Assert.Equal(DocumentRawContentStatus.Unavailable, output.ContentStatus);
		Assert.Null(output.GetRawContent());
		var native = Assert.IsType<JsonElement>(output.GetRawRepresentation());
		Assert.Equal(JsonValueKind.Array, native.ValueKind);
		Assert.Contains("native-bridge-snapshot", output.ProviderName);
		Assert.True(image.CanRead);
	}

	[AppleVisionRecognizeDocumentsFact]
	public async Task ExtractPagesAsync_PdfFinalizesSourceFramesAndKeepsInputOpen()
	{
		using var pdf = await FileSystem.OpenAppPackageFileAsync("DocumentReader/two-pages.pdf");
		using IDocumentExtractionClient client = new AppleVisionRecognizeDocumentsClient();
		using var result = await client.ExtractPagesAsync(pdf, "pdf", "application/pdf")
			.ToDocumentExtractionResultAsync("pdf");
		Assert.Equal([1, 2], result.Pages.Select(page => page.PageNumber));
		Assert.All(result.Pages, page => Assert.Null(page.Markdown));
		Assert.Contains("FIRST PAGE", result.Document.Sections[0].ToMarkdown(), StringComparison.OrdinalIgnoreCase);
		Assert.Contains("SECOND PAGE", result.Document.Sections[1].ToMarkdown(), StringComparison.OrdinalIgnoreCase);
		Assert.Equal(["pdf/page/1", "pdf/page/2"], result.Document.SourcePages.Select(page => page.Identifier));
		Assert.Equal(result.Pages.SelectMany(page => page.Observations).Count(),
			result.Pages.SelectMany(page => page.Observations).Select(item => item.ProviderId).Distinct().Count());
		Assert.True(pdf.CanRead);
	}

	[AppleVisionRecognizeDocumentsFact]
	public async Task ExtractAsync_RawOptOutLeavesSemanticGeometryButNoNativeEscape()
	{
		using var image = await FileSystem.OpenAppPackageFileAsync("DocumentReader/merged-table.png");
		using IDocumentExtractionClient client = new AppleVisionRecognizeDocumentsClient();
		using var result = await client.ExtractAsync(image, "table", "image/png", new() { CaptureRawOutput = false });
		Assert.All(result.Document.SourceOutputs, source =>
		{
			Assert.Equal(DocumentRawContentStatus.Disabled, source.NativeStatus);
			Assert.Equal(DocumentRawContentStatus.Disabled, source.ContentStatus);
			Assert.Null(source.GetRawRepresentation());
			Assert.Null(source.GetRawContent());
		});
		Assert.Empty(result.Document.Artifacts);
		foreach (var element in result.Document.EnumerateContent())
		{
			Assert.All(element.Annotations, annotation => Assert.Null(annotation.GetRawRepresentation()));
			if (element is DocumentTableElement table)
				foreach (var cell in table.Cells)
					if (cell is not null)
						Assert.All(cell.Annotations, annotation => Assert.Null(annotation.GetRawRepresentation()));
		}
		var canonicalTable = Assert.Single(result.Document.EnumerateContent().OfType<DocumentTableElement>());
		Assert.Equal(2, canonicalTable.Cells[0, 0]!.ColumnSpan);
		Assert.True(canonicalTable.Cells.IsCovered(0, 1));
		Assert.NotEmpty(canonicalTable.Cells[0, 0]!.Annotations[0].Regions);
		Assert.True(image.CanRead);
	}
}

internal sealed class AppleVisionRecognizeDocumentsFactAttribute : FactAttribute
{
	public AppleVisionRecognizeDocumentsFactAttribute()
	{
		if (!OperatingSystem.IsIOSVersionAtLeast(26) && !OperatingSystem.IsMacCatalystVersionAtLeast(26))
			Skip = "Apple Vision document recognition requires iOS or Mac Catalyst 26+.";
	}
}
#endif
