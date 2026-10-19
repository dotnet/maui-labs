using System.Text.Json;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.UnitTests;

public class AppleVisionRecognizeDocumentsIngestionMapperTests
{
	[Fact]
	public void MapDocument_ProjectsFinalizedCanonicalPagesAndNeverFabricatesHeaderLevels()
	{
		using var json = JsonDocument.Parse("""
			[{"transcript":"Full transcript","nodes":[
			{"path":"title","kind":"title","text":"Heading"},{"path":"p","kind":"paragraph","text":"Body"}]}]
			""");
		using var result = new[] { AppleVisionRecognizeDocumentsMapper.ToPage(json.RootElement, 3, "source", false) }
			.ToDocumentExtractionResult("source", false);
		var ingestion = AppleVisionRecognizeDocumentsIngestionMapper.MapDocument(result.Document);
		Assert.Equal("source", ingestion.Identifier);
		var page = Assert.Single(ingestion.Sections);
		Assert.Equal(3, page.PageNumber);
		Assert.Contains(page.Elements, item => item.Text == "Heading");
		Assert.Contains(page.Elements, item => item.Text == "Body");
		Assert.Empty(page.Elements.OfType<IngestionDocumentHeader>());
		Assert.All(page.Elements, item => { Assert.False(item.HasMetadata); Assert.Equal(3, item.PageNumber); });
		result.Dispose();
		Assert.Contains("Body", string.Join("\n", page.Elements.Select(item => item.GetMarkdown())));
	}

	[Fact]
	public void MapDocument_UsesFullTranscriptWhenNoSemanticElementsExist()
	{
		using var json = JsonDocument.Parse("""[{"transcript":"Actual transcript","nodes":[]} ]""");
		using var result = new[] { AppleVisionRecognizeDocumentsMapper.ToPage(json.RootElement, 2, "input", false) }
			.ToDocumentExtractionResult("input", false);
		var paragraph = Assert.IsType<IngestionDocumentParagraph>(
			Assert.Single(Assert.Single(AppleVisionRecognizeDocumentsIngestionMapper.MapDocument(result.Document).Sections).Elements));
		Assert.Equal("Actual transcript", paragraph.Text);
		Assert.Equal(2, paragraph.PageNumber);
	}

	[Fact]
	public void MapDocument_UsesCanonicalSpanMarkdownAndLogicalGridWithoutMetadata()
	{
		var cells = new[]
		{
			new DocumentExtractionTableCell(0, 0, new DocumentExtractionText("cell", 0, DocumentTextKind.Paragraph, "Merged", "Merged"), 1, 2),
			new DocumentExtractionTableCell(0, 2, null),
			new DocumentExtractionTableCell(1, 2, new DocumentExtractionText("other", 0, DocumentTextKind.Paragraph, "8", "8")),
		};
		using var result = new[] { new DocumentExtractionPage(3, [new DocumentExtractionTable("table", 0, "", 2, 3, cells)],
			sourcePage: new DocumentSourcePage("input/page/3", 3)) }.ToDocumentExtractionResult("input");
		var table = Assert.IsType<IngestionDocumentTable>(Assert.Single(
			Assert.Single(AppleVisionRecognizeDocumentsIngestionMapper.MapDocument(result.Document).Sections).Elements));
		Assert.Equal("Merged", table.Cells[0, 0]!.Text);
		Assert.Null(table.Cells[0, 1]);
		Assert.Equal("8", table.Cells[1, 2]!.Text);
		Assert.Contains("colspan=\"2\"", table.GetMarkdown());
		Assert.False(table.HasMetadata);
	}
}
