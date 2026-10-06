#if IOS || MACCATALYST
using System.Text.Json;
using Foundation;
using Microsoft.Maui.Essentials.AI.Internal.DocumentExtraction;
using Microsoft.Maui.Storage;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

public class AppleVisionRecognizeDocumentsClientTests
{
	[AppleVisionRecognizeDocumentsFact]
	public async Task ExtractAsync_Image_PreservesRichSnapshotAndCapabilities()
	{
		using var client = new AppleVisionRecognizeDocumentsClient();
		var capabilities = client.GetCapabilities();
		Assert.Same(capabilities, client.GetService(typeof(AppleVisionRecognizeDocumentsCapabilities)));
		var metadata = Assert.IsType<DocumentExtractionClientMetadata>(
			client.GetService(typeof(DocumentExtractionClientMetadata)));
		Assert.Equal("apple.vision", metadata.ProviderName);
		Assert.Equal("recognize-documents", metadata.DefaultModelId);
		Assert.NotEmpty(capabilities.SupportedRecognitionLanguages);
		Assert.NotEmpty(capabilities.SupportedBarcodeSymbologies);
		Assert.Contains(1, capabilities.SupportedRevisions);
		using var image = await FileSystem.OpenAppPackageFileAsync("DocumentReader/headings.png");
		var options = new DocumentExtractionOptions
		{
			AdditionalProperties = new()
			{
				[AppleVisionRecognizeDocumentsOptions.MaximumCandidateCount] = 2,
				[AppleVisionRecognizeDocumentsOptions.Revision] = 1,
			},
		};
		var result = await client.ExtractAsync(image, "image/png", options);
		var page = Assert.Single(result.Pages);
		Assert.Equal(1, result.Usage?.PagesProcessed);
		Assert.Equal(1, result.AdditionalProperties!["apple.vision.totalPages"]);
		Assert.False(page.AdditionalProperties!.ContainsKey("apple.pdf.pageInfo"));
		Assert.Contains("DOCUMENT EXTRACTION EVALUATION", page.Text, StringComparison.OrdinalIgnoreCase);
		Assert.Equal(DocumentCoordinateUnit.Normalized, page.CoordinateUnit);
		Assert.Equal(DocumentCoordinateOrigin.BottomLeft, page.CoordinateOrigin);
		Assert.True(Assert.IsType<int>(page.AdditionalProperties["apple.sourcePixelWidth"]) > 0);
		Assert.True(Assert.IsType<int>(page.AdditionalProperties["apple.sourcePixelHeight"]) > 0);
		var observations = Assert.IsType<AppleVisionRecognizeDocumentsObservationSnapshot[]>(
			page.AdditionalProperties["apple.vision.observations"]);
		Assert.NotEmpty(observations);
		Assert.All(observations, observation =>
		{
			Assert.NotNull(observation.Id);
			Assert.NotNull(observation.Confidence);
			Assert.True(observation.AdditionalProperties.ContainsKey("projectedNodeCount"));
			Assert.NotEmpty(observation.Nodes);
			Assert.All(observation.Nodes, node => Assert.Equal(JsonValueKind.Object,
				Assert.IsType<JsonElement>(node.RawRepresentation).ValueKind));
		});
		Assert.Contains(page.Elements, element => element is DocumentBlock block && block.Kind == DocumentBlockKind.Title);
		Assert.Equal(JsonValueKind.Array, Assert.IsType<JsonElement>(page.RawRepresentation).ValueKind);
		Assert.Equal(2, Assert.IsType<DocumentExtractionOptions>(
			result.AdditionalProperties["apple.vision.options"]).AdditionalProperties![AppleVisionRecognizeDocumentsOptions.MaximumCandidateCount]);
		Assert.True(image.CanRead);

		using var rejectedImage = await FileSystem.OpenAppPackageFileAsync("DocumentReader/headings.png");
		var unsupportedRevision = new DocumentExtractionOptions
		{
			AdditionalProperties = new() { [AppleVisionRecognizeDocumentsOptions.Revision] = 2 },
		};
		var error = await Assert.ThrowsAsync<NSErrorException>(() =>
			client.ExtractAsync(rejectedImage, "image/png", unsupportedRevision));
		Assert.Equal(nameof(RecognizeDocumentsRequestNative), error.Error.Domain);
		Assert.Equal((nint)RecognizeDocumentsRequestErrorNative.InvalidRevision, error.Error.Code);
	}

	[AppleVisionRecognizeDocumentsFact]
	public async Task ExtractPagesAsync_Pdf_ReportsProgressAndPdfFactsWithoutClosingStream()
	{
		using var pdf = await FileSystem.OpenAppPackageFileAsync("DocumentReader/two-pages.pdf");
		using var client = new AppleVisionRecognizeDocumentsClient();
		var updates = new List<DocumentExtractionPageResult>();
		await foreach (var update in client.ExtractPagesAsync(pdf, "application/pdf"))
			updates.Add(update);
		Assert.Equal([1, 2], updates.Select(update => update.Page.PageNumber));
		Assert.Equal([1, 2], updates.Select(update => update.PagesProcessed));
		Assert.All(updates, update =>
		{
			Assert.Equal(2, update.TotalPages);
			var pageInfo = Assert.IsType<AppleVisionRecognizeDocumentsPdfPageInfo>(
				update.Page.AdditionalProperties!["apple.pdf.pageInfo"]);
			Assert.Equal(200, pageInfo.RequestedDpi);
			Assert.True(pageInfo.EffectiveDpi > 0);
			Assert.True(pageInfo.WidthPoints > 0);
			Assert.True(pageInfo.HeightPoints > 0);
			Assert.Equal("Crop", pageInfo.DisplayBox);
			Assert.Equal(JsonValueKind.Array, Assert.IsType<JsonElement>(update.RawRepresentation).ValueKind);
		});
		Assert.Contains("FIRST PAGE", updates[0].Page.Text, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("SECOND PAGE", updates[1].Page.Text, StringComparison.OrdinalIgnoreCase);
		Assert.True(pdf.CanRead);
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
