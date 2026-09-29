#if IOS || MACCATALYST

using System.Text.Json;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Storage;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

public sealed class AppleVisionDocumentCorpusTests(ITestOutputHelper output)
{
	[Fact]
	public async Task CorpusManifest_ReferencesPackagedFixtures()
	{
		await using var manifestStream = await FileSystem.Current.OpenAppPackageFileAsync(
			$"{AppleVisionDocumentCorpus.AssetRoot}manifest.json");
		using var manifest = await JsonDocument.ParseAsync(manifestStream);
		var fixtures = manifest.RootElement.GetProperty("fixtures").EnumerateArray().ToArray();

		Assert.Equal(9, fixtures.Length);
		foreach (var fixture in fixtures)
		{
			var fileName = fixture.GetProperty("file").GetString();
			Assert.False(string.IsNullOrWhiteSpace(fileName));
			await using var imageStream = await FileSystem.Current.OpenAppPackageFileAsync(
				$"{AppleVisionDocumentCorpus.AssetRoot}{fileName}");
			Assert.True(imageStream.Length > 0);
		}
	}

	[Fact]
	public async Task ExtractAsync_HeadingsFixture_MapsTitleAndParagraphs()
	{
		if (!AppleVisionDocumentCorpus.IsSupported())
		{
			return;
		}

		var page = await AppleVisionDocumentCorpus.ExtractAsync(
			"headings-and-paragraphs.png");

		Assert.Contains("provider fidelity", page.Text, StringComparison.OrdinalIgnoreCase);
		var blocks = page.Elements.OfType<DocumentBlock>().ToArray();
		var title = Assert.Single(
			blocks,
			static block => block.Kind == DocumentBlockKind.Title);
		Assert.Equal("DOCUMENT EXTRACTION EVALUATION", title.Text);
		Assert.True(blocks.Count(static block =>
			block.Kind == DocumentBlockKind.Paragraph) >= 4);
		WriteDiagnostics("headings-and-paragraphs.png", page);
	}

	[Fact]
	public async Task ExtractAsync_TableFixture_MapsRowsColumnsAndCells()
	{
		if (!AppleVisionDocumentCorpus.IsSupported())
		{
			return;
		}

		var page = await AppleVisionDocumentCorpus.ExtractAsync("table.png");

		var table = Assert.Single(page.Elements.OfType<DocumentTable>());
		Assert.Equal(4, table.RowCount);
		Assert.Equal(3, table.ColumnCount);
		Assert.Equal(12, table.Cells?.Count);
		Assert.Contains(table.Cells!, static cell =>
			cell.Content.Contains("Notebook", StringComparison.OrdinalIgnoreCase));
		Assert.Contains(table.Cells!, static cell =>
			cell.Content.Contains("Low stock", StringComparison.OrdinalIgnoreCase));
		WriteDiagnostics("table.png", page);
	}

	[Fact]
	public async Task ExtractAsync_DetectedDataFixture_PreservesSemanticTypes()
	{
		if (!AppleVisionDocumentCorpus.IsSupported())
		{
			return;
		}

		var page = await AppleVisionDocumentCorpus.ExtractAsync("detected-data.png");

		var matches = AppleVisionDocumentCorpus.GetDetectedData(page);
		var types = matches
			.Select(static match => match.GetProperty("type").GetString())
			.ToHashSet(StringComparer.Ordinal);
		Assert.Contains("link", types);
		Assert.Contains("emailAddress", types);
		Assert.Contains("phoneNumber", types);
		Assert.Contains("postalAddress", types);
		Assert.Contains("calendarEvent", types);
		Assert.Contains("moneyAmount", types);
		Assert.Contains(matches, static match =>
			AppleVisionDocumentCorpus.GetOptionalString(match, "url") ==
			"https://www.microsoft.com");
		Assert.Contains(matches, static match =>
			AppleVisionDocumentCorpus.GetOptionalString(match, "value") ==
			"travel@example.com");
		Assert.Contains(matches, static match =>
			AppleVisionDocumentCorpus.GetOptionalString(match, "currency") == "USD");
		WriteDiagnostics("detected-data.png", page);
	}

	[Fact]
	public async Task ExtractAsync_BarcodeFixture_MapsQrAndCode128()
	{
		if (!AppleVisionDocumentCorpus.IsSupported())
		{
			return;
		}
#if IOS
		if (DeviceInfo.Current.DeviceType == DeviceType.Virtual)
		{
			return;
		}
#endif

		var options = new DocumentExtractionOptions()
			.WithBarcodeDetection(true, symbologies: ["qr", "code128"]);
		var page = await AppleVisionDocumentCorpus.ExtractAsync("barcodes.png", options);

		var barcodes = page.Elements
			.OfType<DocumentBlock>()
			.Where(static block => block.Kind?.Value == "barcode")
			.ToArray();
		Assert.Equal(2, barcodes.Length);
		Assert.Contains(barcodes, static barcode =>
			GetStringProperty(barcode, "apple.vision.barcodeSymbology") == "qr" &&
			GetStringProperty(barcode, "apple.vision.barcodePayload") ==
				"https://example.com/meai/vision-corpus");
		Assert.Contains(barcodes, static barcode =>
			GetStringProperty(barcode, "apple.vision.barcodeSymbology") == "code128" &&
			GetStringProperty(barcode, "apple.vision.barcodePayload") ==
				"MEAI-VISION-2026");
		WriteDiagnostics("barcodes.png", page);
	}

	[Fact]
	public async Task ExtractAsync_MixedFixture_MapsHeterogeneousDocument()
	{
		if (!AppleVisionDocumentCorpus.IsSupported())
		{
			return;
		}

		var page = await AppleVisionDocumentCorpus.ExtractAsync("mixed-document.png");

		Assert.Single(
			page.Elements.OfType<DocumentBlock>(),
			static block => block.Kind == DocumentBlockKind.Title);
		var table = Assert.Single(page.Elements.OfType<DocumentTable>());
		Assert.Equal(3, table.RowCount);
		Assert.Equal(2, table.ColumnCount);
		var listItems = page.Elements
			.OfType<DocumentBlock>()
			.Where(static block => block.Kind?.Value == "listItem")
			.ToArray();
		Assert.Equal(
			["Bring identification", "Arrive early", "Keep the receipt"],
			listItems.Select(static item =>
				GetStringProperty(item, "apple.vision.itemString") ?? string.Empty).ToArray());
		var detectedTypes = AppleVisionDocumentCorpus.GetDetectedData(page)
			.Select(static match => match.GetProperty("type").GetString())
			.ToHashSet(StringComparer.Ordinal);
		Assert.Contains("link", detectedTypes);
		Assert.Contains("emailAddress", detectedTypes);
		WriteDiagnostics("mixed-document.png", page);
	}

	private static string? GetStringProperty(DocumentElement element, string key) =>
		element.AdditionalProperties?.TryGetValue(key, out var value) == true
			? value as string
			: null;

	private void WriteDiagnostics(string assetName, DocumentPage page)
	{
		var nodes = AppleVisionDocumentCorpus.GetRawNodes(page);
		var diagnostics = new
		{
			Asset = assetName,
			Transcript = page.Text,
			ProjectedNodeCount = AppleVisionDocumentCorpus.GetLongProperty(
				page,
				"apple.vision.projectedNodeCount"),
			MaximumTraversalDepth = AppleVisionDocumentCorpus.GetLongProperty(
				page,
				"apple.vision.maximumTraversalDepth"),
			RepeatedContainersPruned = AppleVisionDocumentCorpus.GetLongProperty(
				page,
				"apple.vision.repeatedContainersPruned"),
			NodeKinds = nodes
				.GroupBy(static node =>
					node.GetProperty("kind").GetString() ?? "unknown")
				.ToDictionary(static group => group.Key, static group => group.Count()),
			DetectedData = AppleVisionDocumentCorpus.GetDetectedData(page),
			NormalizedTypes = AppleVisionDocumentCorpus
				.EnumerateElements(page.Elements)
				.GroupBy(static element => element.GetType().Name)
				.ToDictionary(static group => group.Key, static group => group.Count()),
		};
		output.WriteLine(JsonSerializer.Serialize(
			diagnostics,
			new JsonSerializerOptions { WriteIndented = true }));
	}
}

#endif
