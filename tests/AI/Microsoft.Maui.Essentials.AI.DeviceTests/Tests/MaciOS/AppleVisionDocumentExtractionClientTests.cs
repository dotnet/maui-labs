#if IOS || MACCATALYST
using System.Text.Json;
using Microsoft.Maui.Essentials.AI.Internal.DocumentExtraction;
using Microsoft.Maui.Storage;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

public class AppleVisionDocumentExtractionClientTests
{
	[AppleVisionFact]
	public async Task ExtractAsync_Image_PreservesRichSnapshotAndCapabilities()
	{
		var client = new AppleVisionDocumentExtractionClient();
		var capabilities = client.GetCapabilities();
		Assert.NotEmpty(capabilities.RecognitionLanguages);
		Assert.NotEmpty(capabilities.BarcodeSymbologies);
		Assert.Contains(1, capabilities.Revisions);
		using var image = await FileSystem.OpenAppPackageFileAsync("DocumentReader/headings.png");
		var options = new DocumentExtractionOptions { MaximumCandidateCount = 2, Revision = 1 };
		var result = await client.ExtractAsync(image, "image/png", options);
		var page = Assert.Single(result.Pages);
		Assert.Equal(1, result.Usage.PagesProcessed);
		Assert.Equal(1, result.Usage.TotalPages);
		Assert.Null(page.PdfPage);
		Assert.Contains("DOCUMENT EXTRACTION EVALUATION", page.Text, StringComparison.OrdinalIgnoreCase);
		Assert.Equal(DocumentCoordinateUnit.Normalized, page.CoordinateUnit);
		Assert.Equal(DocumentCoordinateOrigin.BottomLeft, page.CoordinateOrigin);
		Assert.True(Assert.IsType<int>(page.AdditionalProperties["apple.sourcePixelWidth"]) > 0);
		Assert.True(Assert.IsType<int>(page.AdditionalProperties["apple.sourcePixelHeight"]) > 0);
		Assert.NotEmpty(page.Observations);
		Assert.All(page.Observations, observation =>
		{
			Assert.NotNull(observation.Id);
			Assert.NotNull(observation.Confidence);
			Assert.True(observation.AdditionalProperties.ContainsKey("projectedNodeCount"));
			Assert.NotEmpty(observation.Nodes);
			Assert.All(observation.Nodes, node => Assert.Equal(JsonValueKind.Object,
				Assert.IsType<JsonElement>(node.RawRepresentation).ValueKind));
		});
		Assert.Contains(page.Elements, element => element is DocumentBlock { Kind: DocumentBlockKind.Title });
		Assert.Equal(JsonValueKind.Array, Assert.IsType<JsonElement>(page.RawRepresentation).ValueKind);
		Assert.Equal(2, Assert.IsType<DocumentExtractionOptions>(
			result.AdditionalProperties["apple.vision.options"]).MaximumCandidateCount);
		Assert.True(image.CanRead);
	}

	[AppleVisionFact]
	public async Task ExtractPagesAsync_Pdf_ReportsProgressAndPdfFactsWithoutClosingStream()
	{
		using var pdf = await FileSystem.OpenAppPackageFileAsync("DocumentReader/two-pages.pdf");
		var client = new AppleVisionDocumentExtractionClient();
		var updates = new List<DocumentExtractionPageResult>();
		await foreach (var update in client.ExtractPagesAsync(pdf, "application/pdf"))
			updates.Add(update);
		Assert.Equal([1, 2], updates.Select(update => update.Page.PageNumber));
		Assert.Equal([1, 2], updates.Select(update => update.Usage.PagesProcessed));
		Assert.All(updates, update =>
		{
			Assert.Equal(2, update.Usage.TotalPages);
			Assert.NotNull(update.Page.PdfPage);
			Assert.Equal(200, update.Page.PdfPage.RequestedDpi);
			Assert.True(update.Page.PdfPage.EffectiveDpi > 0);
			Assert.True(update.Page.PdfPage.WidthPoints > 0);
			Assert.True(update.Page.PdfPage.HeightPoints > 0);
			Assert.Equal("Crop", update.Page.PdfPage.DisplayBox);
			Assert.Equal(JsonValueKind.Array, Assert.IsType<JsonElement>(update.RawRepresentation).ValueKind);
		});
		Assert.Contains("FIRST PAGE", updates[0].Page.Text, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("SECOND PAGE", updates[1].Page.Text, StringComparison.OrdinalIgnoreCase);
		Assert.True(pdf.CanRead);
	}
}

internal sealed class AppleVisionFactAttribute : FactAttribute
{
	public AppleVisionFactAttribute()
	{
		if (!OperatingSystem.IsIOSVersionAtLeast(26) && !OperatingSystem.IsMacCatalystVersionAtLeast(26))
			Skip = "Apple Vision document recognition requires iOS or Mac Catalyst 26+.";
	}
}
#endif
