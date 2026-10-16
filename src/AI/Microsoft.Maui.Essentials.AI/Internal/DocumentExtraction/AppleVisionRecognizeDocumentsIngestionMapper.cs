using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.Documents;

namespace Microsoft.Maui.Essentials.AI;

internal static class AppleVisionRecognizeDocumentsIngestionMapper
{
	internal static IngestionDocument MapDocument(Document source)
	{
		var result = new IngestionDocument(source.Identifier);
		for (var index = 0; index < source.Sections.Count; index++)
		{
			var pageNumber = source.SourcePages.Count > index ? source.SourcePages[index].PageNumber : index + 1;
			result.Sections.Add(MapPage(source.Sections[index], pageNumber));
		}
		return result;
	}

	internal static IngestionDocumentSection MapPage(DocumentSection page, int pageNumber)
	{
		var section = new IngestionDocumentSection { PageNumber = pageNumber };
		foreach (var element in page.Elements)
		{
			if (element is DocumentExtensionElement { Type: "apple.vision.list" } list &&
				list.Text is { Length: > 0 } listText && listText.Split('\n').All(item =>
					page.Elements.OfType<DocumentTextElement>().Any(text => text.Text?.Contains(item, StringComparison.Ordinal) == true)))
				continue;
			switch (element)
			{
				case DocumentTextElement text when text.Kind == DocumentTextKind.Header && text.HeaderLevel is { } level:
					section.Elements.Add(new IngestionDocumentHeader(text.GetMarkdown())
						{ Level = level, Text = text.Text, PageNumber = pageNumber });
					break;
				case DocumentTableElement table:
					var cells = new IngestionDocumentElement?[table.Cells.RowCount, table.Cells.ColumnCount];
					foreach (var cell in table.Cells)
						if (cell?.Content is { } content)
							cells[cell.RowIndex, cell.ColumnIndex] = Paragraph(content.ToMarkdown(), content.Text, pageNumber);
					section.Elements.Add(new IngestionDocumentTable(table.ToMarkdown(), cells) { PageNumber = pageNumber });
					break;
				default:
					if (element.ToMarkdown().Length > 0 || element.Text is { Length: > 0 })
						section.Elements.Add(Paragraph(element.ToMarkdown(), element.Text, pageNumber));
					break;
			}
		}
		if (section.Elements.Count == 0 && !string.IsNullOrWhiteSpace(page.StoredMarkdown))
			section.Elements.Add(Paragraph(page.StoredMarkdown, page.StoredMarkdown, pageNumber));
		return section;
	}

	private static IngestionDocumentParagraph Paragraph(string markdown, string? text, int pageNumber) =>
		new(markdown) { Text = text, PageNumber = pageNumber };
}
