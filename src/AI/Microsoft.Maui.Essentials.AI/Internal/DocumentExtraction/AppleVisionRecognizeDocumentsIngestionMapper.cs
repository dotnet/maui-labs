using System.Text;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.DocumentExtraction;

namespace Microsoft.Maui.Essentials.AI;

internal static class AppleVisionRecognizeDocumentsIngestionMapper
{
	internal static IngestionDocumentSection MapPage(DocumentPage page)
	{
		var pageNumber = page.PageNumber;
		var section = new IngestionDocumentSection { PageNumber = pageNumber };
		foreach (var element in page.Elements)
		{
			switch (element)
			{
				case DocumentBlock block when block.Kind == DocumentBlockKind.Title:
					section.Elements.Add(new IngestionDocumentHeader(block.Text)
						{ Level = 1, Text = block.Text, PageNumber = pageNumber });
					break;
				case DocumentTable table:
					section.Elements.Add(ReadTable(table, pageNumber));
					break;
				case DocumentBlock block when block.Kind == DocumentBlockKind.Paragraph:
					section.Elements.Add(Paragraph(block.Text, pageNumber));
					break;
			}
		}
		if (section.Elements.Count == 0 && !string.IsNullOrWhiteSpace(page.Text))
			section.Elements.Add(Paragraph(page.Text, pageNumber));
		return section;
	}

	private static IngestionDocumentParagraph Paragraph(string text, int pageNumber) =>
		new(text) { Text = text, PageNumber = pageNumber };

	private static IngestionDocumentTable ReadTable(DocumentTable table, int pageNumber)
	{
		var rows = table.RowCount;
		var columns = table.ColumnCount;
		var cells = new IngestionDocumentElement?[rows, columns];
		foreach (var cell in table.Cells ?? [])
			if (!string.IsNullOrWhiteSpace(cell.Content))
				cells[cell.RowIndex, cell.ColumnIndex] = Paragraph(cell.Content, pageNumber);

		if (table.MarkdownRepresentation is { } representation)
			return new IngestionDocumentTable(representation, cells) { PageNumber = pageNumber };

		var markdown = new StringBuilder();
		for (var row = 0; row < rows; row++)
		{
			markdown.Append('|');
			for (var column = 0; column < columns; column++)
			{
				var text = cells[row, column]?.GetMarkdown() ?? "";
				markdown.Append(' ').Append(text.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ")).Append(" |");
			}
			markdown.AppendLine();
			if (row == 0)
			{
				markdown.Append('|');
				for (var column = 0; column < columns; column++)
					markdown.Append(" --- |");
				markdown.AppendLine();
			}
		}
		return new IngestionDocumentTable(markdown.ToString().TrimEnd(), cells) { PageNumber = pageNumber };
	}
}
