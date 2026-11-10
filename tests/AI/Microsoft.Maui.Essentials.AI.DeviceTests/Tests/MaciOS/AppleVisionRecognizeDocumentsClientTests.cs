#if IOS || MACCATALYST
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;
using Microsoft.Maui.Storage;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

public class AppleVisionRecognizeDocumentsClientTests
{
	private static string Text(ExtractionDocumentSection section) =>
		string.Join("\n", section.Elements.Select(element => (element.Content as TextContent)?.Text));

	[AppleVisionRecognizeDocumentsFact]
	public async Task ExtractAsync_ImagePreservesSemanticIntersectionAndDetachedNativeSnapshot()
	{
		using DocumentExtractionClient client = new AppleVisionRecognizeDocumentsClient();
		Assert.Same(client, client.GetService(typeof(DocumentExtractionClient)));
		using var image = await FileSystem.OpenAppPackageFileAsync("DocumentReader/headings.png");
		var result = await client.ExtractAsync(image, "headings", "image/png");
		client.Dispose();
		var section = Assert.Single(result.Sections);
		var frame = Assert.Single(result.SourcePages);
		Assert.Contains("DOCUMENT EXTRACTION EVALUATION", Text(section), StringComparison.OrdinalIgnoreCase);
		Assert.Equal(ExtractionDocumentCoordinateUnit.Normalized, frame.CoordinateUnit);
		Assert.Equal(ExtractionDocumentCoordinateOrigin.BottomLeft, frame.CoordinateOrigin);
		Assert.Equal(new ExtractionDocumentPageDimensions(1, 1), frame.Dimensions);
		Assert.Contains(section.Elements, item => item is ExtractionDocumentTextElement { Role: "heading", HeadingLevel: null });
		Assert.Empty(result.Metadata);
		Assert.Null(section.Content);
		Assert.All(result.EnumerateContent(), element => Assert.Null(element.Content?.AdditionalProperties));
		var native = Assert.Single(Assert.IsType<JsonElement[]>(result.RawRepresentation));
		Assert.Equal(JsonValueKind.Array, native.ValueKind);
		Assert.Equal(JsonValueKind.Array, Assert.IsType<JsonElement>(section.Sources[0].GetRawRepresentation()).ValueKind);
		Assert.True(image.CanRead);
	}

	[AppleVisionRecognizeDocumentsFact]
	public async Task ExtractAsync_PdfPreservesPageOrderSourceFramesAndCallerInput()
	{
		using var pdf = await FileSystem.OpenAppPackageFileAsync("DocumentReader/two-pages.pdf");
		using DocumentExtractionClient client = new AppleVisionRecognizeDocumentsClient();
		var result = await client.ExtractAsync(pdf, "pdf", "application/pdf");
		Assert.Equal([1, 2], result.SourcePages.Select(page => page.PageNumber));
		Assert.Contains("FIRST PAGE", Text(result.Sections[0]), StringComparison.OrdinalIgnoreCase);
		Assert.Contains("SECOND PAGE", Text(result.Sections[1]), StringComparison.OrdinalIgnoreCase);
		Assert.Equal(["pdf/page/1", "pdf/page/2"], result.SourcePages.Select(page => page.Identifier));
		Assert.Same(result.SourcePages[0], result.Sections[0].Sources[0].Page);
		Assert.Same(result.SourcePages[1], result.Sections[1].Sources[0].Page);
		Assert.True(pdf.CanRead);
	}

	[AppleVisionRecognizeDocumentsFact]
	public async Task ExtractAsync_TablePreservesGeometrySpansAndNativeDataWithoutFabricatedAssets()
	{
		using var image = await FileSystem.OpenAppPackageFileAsync("DocumentReader/merged-table.png");
		using DocumentExtractionClient client = new AppleVisionRecognizeDocumentsClient();
		var result = await client.ExtractAsync(image, "table", "image/png");
		Assert.Empty(result.EnumerateContent().OfType<ExtractionDocumentImageElement>());
		var table = Assert.Single(result.EnumerateContent().OfType<ExtractionDocumentTableElement>());
		Assert.Equal(2, table.Cells[0, 0]!.ColumnSpan);
		Assert.Null(table.Cells[0, 1]);
		var source = Assert.Single(table.Cells[0, 0]!.Sources);
		Assert.NotEmpty(source.Polygon);
		Assert.Equal("tableCell", Assert.IsType<JsonElement>(source.GetRawRepresentation()).GetProperty("kind").GetString());
		Assert.All(table.Cells, cell => { if (cell is not null) Assert.Null(cell.Role); });
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
