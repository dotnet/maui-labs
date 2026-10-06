#if IOS || MACCATALYST
using Microsoft.Extensions.DataIngestion;
using Microsoft.Maui.Storage;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

public class AppleVisionDocumentReaderTests
{
	private static bool IsSupported() =>
		OperatingSystem.IsIOSVersionAtLeast(26) || OperatingSystem.IsMacCatalystVersionAtLeast(26);

	private static Task<Stream> OpenAsync(string name) =>
		FileSystem.OpenAppPackageFileAsync($"DocumentReader/{name}");

	private static int CountOccurrences(string text, string value)
	{
		var count = 0;
		var start = 0;
		while ((start = text.IndexOf(value, start, StringComparison.OrdinalIgnoreCase)) >= 0)
		{
			count++;
			start += value.Length;
		}
		return count;
	}

	private static string Content(IngestionDocumentSection section) =>
		string.Join("\n", section.Elements.Select(element => element.GetMarkdown()));

	[Fact]
	public async Task ReadAsync_Image_PreservesIdentifierAndText()
	{
		if (!IsSupported()) return;
		using var image = await OpenAsync("headings.png");
		var document = await new AppleVisionDocumentReader()
			.ReadAsync(image, "sample-image", "image/png");
		Assert.Equal("sample-image", document.Identifier);
		var section = Assert.Single(document.Sections);
		Assert.Equal(1, section.PageNumber);
		Assert.Contains(section.Elements, element =>
			element is IngestionDocumentHeader or IngestionDocumentParagraph);
		Assert.Equal(1, CountOccurrences(Content(section), "DOCUMENT EXTRACTION EVALUATION"));
		Assert.Single(section.Elements.OfType<IngestionDocumentHeader>());
	}

	[Fact]
	public async Task ReadAsync_Table_PreservesCellsAndMarkdown()
	{
		if (!IsSupported()) return;
		using var image = await OpenAsync("table.png");
		var document = await new AppleVisionDocumentReader()
			.ReadAsync(image, "table-image", "image/png");
		var section = Assert.Single(document.Sections);
		var table = Assert.Single(section.Elements.OfType<IngestionDocumentTable>());
		Assert.True(table.Cells.GetLength(0) > 1);
		Assert.True(table.Cells.GetLength(1) > 1);
		Assert.Contains("|", table.GetMarkdown());
		Assert.NotNull(table.Cells[0, 0]);
		Assert.Equal(1, CountOccurrences(Content(section), "Notebook"));
		Assert.Equal(1, CountOccurrences(Content(section), "Low stock"));
		Assert.Equal(1, CountOccurrences(Content(section), "QUARTERLY INVENTORY"));
	}

	[Fact]
	public async Task ReadAsync_List_EmitsEachItemAndSurroundingTextOnce()
	{
		if (!IsSupported()) return;
		using var image = await OpenAsync("flat-list.png");
		var document = await new AppleVisionDocumentReader()
			.ReadAsync(image, "list-image", "image/png");
		var section = Assert.Single(document.Sections);
		foreach (var item in new[] { "Apples", "Coffee", "Bread", "Please purchase", "Keep the receipt" })
			Assert.Equal(1, CountOccurrences(Content(section), item));
		Assert.Equal(1, CountOccurrences(Content(section), "WEEKEND SHOPPING"));
	}

	[Fact]
	public async Task ReadAsync_MergedTable_KeepsLogicalColumnPositions()
	{
		if (!IsSupported()) return;
		using var image = await OpenAsync("merged-table.png");
		var document = await new AppleVisionDocumentReader()
			.ReadAsync(image, "merged-image", "image/png");
		var section = Assert.Single(document.Sections);
		var table = Assert.Single(section.Elements.OfType<IngestionDocumentTable>());
		Assert.Equal(3, table.Cells.GetLength(0));
		Assert.Equal(3, table.Cells.GetLength(1));
		Assert.Equal("Merged header", table.Cells[0, 0]?.GetMarkdown());
		Assert.Null(table.Cells[0, 1]);
		Assert.Equal("Count", table.Cells[0, 2]?.GetMarkdown());
		Assert.Equal("Apples", table.Cells[1, 0]?.GetMarkdown());
		Assert.Equal(1, CountOccurrences(Content(section), "Merged header"));
		Assert.Equal(1, CountOccurrences(Content(section), "Apples"));
	}

	[Fact]
	public async Task ReadAsync_MultiPagePdf_CreatesOrderedSections()
	{
		if (!IsSupported()) return;
		using var pdf = await OpenAsync("two-pages.pdf");
		var document = await new AppleVisionDocumentReader()
			.ReadAsync(pdf, "pdf-identifier", "application/pdf");
		Assert.Equal("pdf-identifier", document.Identifier);
		Assert.Equal(2, document.Sections.Count);
		Assert.Equal(1, document.Sections[0].PageNumber);
		Assert.Equal(2, document.Sections[1].PageNumber);
		Assert.Equal(1, CountOccurrences(Content(document.Sections[0]), "FIRST PAGE"));
		Assert.Equal(1, CountOccurrences(Content(document.Sections[1]), "SECOND PAGE"));
	}

	[Fact]
	public async Task ReadAsync_UnsupportedMediaAndCorruptInput_ThrowExplicitErrors()
	{
		if (!IsSupported()) return;
		var reader = new AppleVisionDocumentReader();
		using var unsupported = new MemoryStream([1, 2, 3]);
		await Assert.ThrowsAsync<NotSupportedException>(() =>
			reader.ReadAsync(unsupported, "bad", "text/plain"));
		using var corruptImage = new MemoryStream([1, 2, 3]);
		await Assert.ThrowsAsync<InvalidDataException>(() =>
			reader.ReadAsync(corruptImage, "bad", "image/png"));
		using var corruptPdf = new MemoryStream([1, 2, 3]);
		await Assert.ThrowsAsync<InvalidDataException>(() =>
			reader.ReadAsync(corruptPdf, "bad", "application/pdf"));
	}

	[Fact]
	public async Task ReadAsync_AlreadyCancelled_DoesNotStartRecognition()
	{
		if (!IsSupported()) return;
		using var image = await OpenAsync("headings.png");
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
			new AppleVisionDocumentReader().ReadAsync(image, "cancelled", "image/png", cancellation.Token));
	}
}
#endif
