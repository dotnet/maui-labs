#if IOS || MACCATALYST
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;
using Microsoft.Maui.Storage;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

public class AppleVisionRecognizeDocumentsClientTests
{
	[AppleVisionRecognizeDocumentsFact]
	public async Task ExtractAsync_ImageUsesProposedTypesWithoutCustomMetadata()
	{
		using IDocumentExtractionClient client = new AppleVisionRecognizeDocumentsClient();
		Assert.Same(client, client.GetService(typeof(AppleVisionRecognizeDocumentsClient)));
		using var image = await FileSystem.OpenAppPackageFileAsync("DocumentReader/headings.png");
		using var result = await client.ExtractAsync(image, "headings.png", "image/png");
		var page = Assert.Single(result.Pages);
		Assert.Equal("headings.png", result.Document.Identifier);
		Assert.Contains("DOCUMENT EXTRACTION EVALUATION", result.Document.ToMarkdown(), StringComparison.OrdinalIgnoreCase);
		Assert.Null(page.Markdown);
		Assert.Contains(page.Observations, element => element is DocumentExtractionText { Kind: DocumentTextKind.Header });
		Assert.False(result.Document.HasMetadata);
		Assert.Empty(result.Document.Evidence);
		Assert.Empty(result.Document.Artifacts);
		Assert.All(page.Observations, element =>
		{
			Assert.Empty(element.PageReferences);
			Assert.Empty(element.Evidence);
			Assert.Empty(element.Artifacts);
			if (element is DocumentExtractionText { Kind: DocumentTextKind.Header } header)
				Assert.Null(header.HeaderLevel);
		});
		Assert.True(image.CanRead);
	}

	[AppleVisionRecognizeDocumentsFact]
	public async Task ExtractPagesAsync_PdfReturnsActualPagesAndPreservesInput()
	{
		using var pdf = await FileSystem.OpenAppPackageFileAsync("DocumentReader/two-pages.pdf");
		using IDocumentExtractionClient client = new AppleVisionRecognizeDocumentsClient();
		var updates = new List<DocumentExtractionPage>();
		await foreach (var update in client.ExtractPagesAsync(pdf, "two-pages.pdf", "application/pdf"))
			updates.Add(update);
		Assert.Equal([1, 2], updates.Select(update => update.PageNumber));
		Assert.All(updates, update =>
		{
			Assert.Null(update.Markdown);
			Assert.Empty(update.PageReferences);
		});
		Assert.Contains("FIRST PAGE", string.Join("\n", updates[0].Observations.Select(item => item.Markdown)),
			StringComparison.OrdinalIgnoreCase);
		Assert.Contains("SECOND PAGE", string.Join("\n", updates[1].Observations.Select(item => item.Markdown)),
			StringComparison.OrdinalIgnoreCase);
		using var result = updates.ToDocumentExtractionResult("two-pages.pdf");
		Assert.Equal(2, result.Document.Sections.Count);
		var ids = updates.SelectMany(page => page.Observations).Select(item => item.ProviderId).ToArray();
		Assert.Equal(ids.Length, ids.Distinct().Count());
		Assert.True(pdf.CanRead);
	}

	[AppleVisionRecognizeDocumentsFact]
	public async Task ExtractAsync_DisposesSharedDocumentWithoutTakingInputOwnership()
	{
		using var image = await FileSystem.OpenAppPackageFileAsync("DocumentReader/headings.png");
		using IDocumentExtractionClient client = new AppleVisionRecognizeDocumentsClient();
		var result = await client.ExtractAsync(image, "shared-document", "image/png");
		Assert.Equal("shared-document", result.Document.Identifier);
		Assert.False(result.IsDisposed);
		Assert.False(result.Document.IsDisposed);
		result.Dispose();
		result.Dispose();
		Assert.True(result.IsDisposed);
		Assert.True(result.Document.IsDisposed);
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
