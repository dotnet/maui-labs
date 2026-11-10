using System.Net;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.Documents;

namespace Microsoft.Maui.Essentials.AI;

internal static class AppleVisionRecognizeDocumentsIngestionMapper
{
	internal static IngestionDocument MapDocument(ExtractionDocument source)
	{
		var result = new IngestionDocument(source.Identifier);
		foreach (var page in source.Sections)
		{
			var pageNumber = page.Sources.FirstOrDefault()?.Page.PageNumber;
			var section = new IngestionDocumentSection { PageNumber = pageNumber };
			foreach (var element in page.Elements)
			{
				if (element is ExtractionDocumentTableElement table)
				{
					var cells = new IngestionDocumentElement?[table.Cells.RowCount, table.Cells.ColumnCount];
					foreach (var cell in table.Cells)
						if (cell?.Content is { } content)
							cells[cell.RowIndex, cell.ColumnIndex] = Paragraph(Text(content), pageNumber);
					section.Elements.Add(new IngestionDocumentTable(RenderTable(table), cells) { PageNumber = pageNumber });
				}
				else if (element is ExtractionDocumentTextElement { Role: "heading", HeadingLevel: { } level } heading)
					section.Elements.Add(new IngestionDocumentHeader(Text(heading))
						{ Level = level, Text = Text(heading), PageNumber = pageNumber });
				else if (Text(element) is { Length: > 0 } text)
					section.Elements.Add(Paragraph(text, pageNumber));
			}
			result.Sections.Add(section);
		}
		return result;
	}

	private static string Text(ExtractionDocumentElement element) => element switch
	{
		ExtractionDocumentSection section => string.Join("\n", section.Elements.Select(Text)),
		ExtractionDocumentTableElement table => RenderTable(table),
		_ => (element.Content as TextContent)?.Text ?? "",
	};

	private static bool ContainsTable(ExtractionDocumentElement? element) => element switch
	{
		ExtractionDocumentTableElement => true,
		ExtractionDocumentSection section => section.Elements.Any(ContainsTable),
		_ => false,
	};

	private static string Html(ExtractionDocumentElement element) => element switch
	{
		ExtractionDocumentTableElement table => RenderTable(table, forceHtml: true),
		ExtractionDocumentSection section => string.Join("\n", section.Elements.Select(Html)),
		_ => WebUtility.HtmlEncode(Text(element)).Replace("\r\n", "\n", StringComparison.Ordinal)
			.Replace("\n", "<br>", StringComparison.Ordinal),
	};

	private static string RenderTable(ExtractionDocumentTableElement table, bool forceHtml = false)
	{
		var useHtml = forceHtml || table.Cells.Any(cell =>
			cell is { RowSpan: > 1 } or { ColumnSpan: > 1 } || ContainsTable(cell?.Content));
		var covered = new bool[table.Cells.RowCount, table.Cells.ColumnCount];
		if (useHtml)
			foreach (var cell in table.Cells)
				if (cell is not null)
					for (var row = cell.RowIndex; row < cell.RowIndex + cell.RowSpan; row++)
						for (var column = cell.ColumnIndex; column < cell.ColumnIndex + cell.ColumnSpan; column++)
							covered[row, column] = true;
		var rows = new List<string>();
		for (var row = 0; row < table.Cells.RowCount; row++)
		{
			var cells = new List<string>();
			for (var column = 0; column < table.Cells.ColumnCount; column++)
			{
				var cell = table.Cells[row, column];
				if (!useHtml)
				{
					var text = cell?.Content is { } content ? Text(content) : "";
					cells.Add(text.Replace("|", "\\|", StringComparison.Ordinal).Replace("\n", "<br>", StringComparison.Ordinal));
				}
				else if (cell is not null)
					cells.Add($"<td rowspan=\"{cell.RowSpan}\" colspan=\"{cell.ColumnSpan}\">{(cell.Content is { } content ? Html(content) : "")}</td>");
				else if (!covered[row, column])
					cells.Add("<td></td>");
			}
			rows.Add(useHtml ? "<tr>" + string.Concat(cells) + "</tr>" : "| " + string.Join(" | ", cells) + " |");
			if (!useHtml && row == 0)
				rows.Add("| " + string.Join(" | ", Enumerable.Repeat("---", table.Cells.ColumnCount)) + " |");
		}
		return useHtml ? "<table>\n" + string.Join("\n", rows) + "\n</table>" : string.Join("\n", rows);
	}

	private static IngestionDocumentParagraph Paragraph(string text, int? pageNumber) =>
		new(text) { Text = text, PageNumber = pageNumber };
}
