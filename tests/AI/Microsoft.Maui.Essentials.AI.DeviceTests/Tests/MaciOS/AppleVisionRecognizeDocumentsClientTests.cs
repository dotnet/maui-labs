#if IOS || MACCATALYST
using System.Text.Json;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Maui.Storage;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

public class AppleVisionRecognizeDocumentsClientTests
{
	[AppleVisionRecognizeDocumentsFact]
	public async Task ExtractAsync_ImageUsesProposedTypesWithoutCustomMetadata()
	{
		using IDocumentExtractionClient client = new AppleVisionRecognizeDocumentsClient();
		var metadata = client.GetService<DocumentExtractionClientMetadata>();
		Assert.Equal("apple.vision", metadata!.ProviderName);
		Assert.Null(metadata.DefaultModelId);
		using var image = await FileSystem.OpenAppPackageFileAsync("DocumentReader/headings.png");
		var result = await client.ExtractAsync(image, "image/png");
		var page = Assert.Single(result.Pages);
		Assert.Contains("DOCUMENT EXTRACTION EVALUATION", page.Text, StringComparison.OrdinalIgnoreCase);
		Assert.Equal(DocumentCoordinateUnit.Normalized, page.CoordinateUnit);
		Assert.Equal(DocumentCoordinateOrigin.BottomLeft, page.CoordinateOrigin);
		Assert.Contains(page.Elements, element => element is DocumentBlock block && block.Kind == DocumentBlockKind.Title);
		Assert.Null(result.AdditionalProperties);
		Assert.Null(page.AdditionalProperties);
		Assert.All(page.Elements, element => Assert.Null(element.AdditionalProperties));
		Assert.Equal(JsonValueKind.Array, Assert.IsType<JsonElement>(page.RawRepresentation).ValueKind);
		Assert.True(image.CanRead);
	}

	[AppleVisionRecognizeDocumentsFact]
	public async Task ExtractPagesAsync_PdfReportsActualPageProgressAndPreservesInput()
	{
		using var pdf = await FileSystem.OpenAppPackageFileAsync("DocumentReader/two-pages.pdf");
		using IDocumentExtractionClient client = new AppleVisionRecognizeDocumentsClient();
		var updates = new List<DocumentExtractionPageResult>();
		await foreach (var update in client.ExtractPagesAsync(pdf, "application/pdf"))
			updates.Add(update);
		Assert.Equal([1, 2], updates.Select(update => update.Page.PageNumber));
		Assert.Equal([1, 2], updates.Select(update => update.PagesProcessed));
		Assert.All(updates, update =>
		{
			Assert.Equal(2, update.TotalPages);
			Assert.Null(update.AdditionalProperties);
			Assert.Null(update.Usage);
		});
		Assert.Contains("FIRST PAGE", updates[0].Page.Text, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("SECOND PAGE", updates[1].Page.Text, StringComparison.OrdinalIgnoreCase);
		Assert.True(pdf.CanRead);
	}

	[AppleVisionRecognizeDocumentsFact]
	public async Task OcrDocumentReader_ExecutesTheUnchangedUpstreamAdapter()
	{
		using var image = await FileSystem.OpenAppPackageFileAsync("DocumentReader/headings.png");
		using IDocumentExtractionClient client = new AppleVisionRecognizeDocumentsClient();
		var document = await new OcrDocumentReader(client).ReadAsync(image, "upstream-ocr", "image/png");
		Assert.Equal("upstream-ocr", document.Identifier);
		var paragraph = Assert.IsType<IngestionDocumentParagraph>(Assert.Single(Assert.Single(document.Sections).Elements));
		Assert.Contains("DOCUMENT EXTRACTION EVALUATION", paragraph.Text, StringComparison.OrdinalIgnoreCase);
		Assert.Equal(1, paragraph.PageNumber);
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
