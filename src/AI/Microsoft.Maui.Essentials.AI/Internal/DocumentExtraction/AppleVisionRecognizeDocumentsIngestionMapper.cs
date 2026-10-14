using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;

namespace Microsoft.Maui.Essentials.AI;

// Local compatibility projection; the upstream proposal does not provide a DataIngestion adapter.
internal static class AppleVisionRecognizeDocumentsIngestionMapper
{
	internal static IngestionDocument MapDocument(DocumentExtractionResult result, CancellationToken cancellationToken = default)
	{
		var document = new IngestionDocument(result.Document.Identifier);
		using var sections = result.Document.Sections.GetEnumerator();
		foreach (var page in result.Pages)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var section = new IngestionDocumentSection { PageNumber = page.PageNumber };
			if (page.Observations.Count > 0)
			{
				if (!sections.MoveNext())
					throw new InvalidDataException("The finalized document is missing a page section.");
				foreach (var element in sections.Current.Elements)
				{
					cancellationToken.ThrowIfCancellationRequested();
					section.Elements.Add(MapElement(element, page.PageNumber));
				}
			}
			document.Sections.Add(section);
		}
		cancellationToken.ThrowIfCancellationRequested();
		return document;
	}

	private static IngestionDocumentElement MapElement(DocumentElement element, int pageNumber)
	{
		if (element is DocumentTableElement table)
		{
			var cells = new IngestionDocumentElement?[table.Cells.RowCount, table.Cells.ColumnCount];
			for (var row = 0; row < table.Cells.RowCount; row++)
				for (var column = 0; column < table.Cells.ColumnCount; column++)
					if (table.Cells[row, column] is { } cell)
						cells[row, column] = MapElement(cell.Content, pageNumber);
			return new IngestionDocumentTable(table.GetMarkdown(), cells) { PageNumber = pageNumber, Text = table.Text };
		}
		if (element is DocumentTextElement { Kind: DocumentTextKind.Header } header)
		{
			var mapped = new IngestionDocumentHeader(header.GetMarkdown()) { Text = header.Text, PageNumber = pageNumber };
			if (header.HeaderLevel is { } level)
				mapped.Level = level;
			return mapped;
		}
		if (element is DocumentTextElement text)
			return new IngestionDocumentParagraph(text.GetMarkdown()) { Text = text.Text, PageNumber = pageNumber };
		throw new NotSupportedException("The local Apple ingestion projection supports text and table elements.");
	}
}
