using DocumentExtractionSample.Models;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Maui.Essentials.AI;

namespace DocumentExtractionSample.Services;

/// <summary>Flattens a <see cref="DocumentExtractionResult"/> (or a single captured <see cref="DocumentPage"/>) into
/// a linear list of <see cref="DocumentTreeNode"/> rows suitable for display in a simple indented list.</summary>
public static class DocumentTreeBuilder
{
	private const int MaxPreviewLength = 160;

	/// <summary>Builds a flattened tree for every page in <paramref name="result"/>.</summary>
	public static List<DocumentTreeNode> BuildTree(DocumentExtractionResult result)
	{
		ArgumentNullException.ThrowIfNull(result);

		var nodes = new List<DocumentTreeNode>();
		foreach (var page in result.Pages)
		{
			AppendPage(nodes, page, $"Page {page.PageNumber}");
		}
		return nodes;
	}

	/// <summary>Builds a flattened tree for a single page, using a custom root title (for example a scanned-camera page).</summary>
	public static List<DocumentTreeNode> BuildTree(DocumentPage page, string pageTitle)
	{
		ArgumentNullException.ThrowIfNull(page);
		ArgumentException.ThrowIfNullOrWhiteSpace(pageTitle);

		var nodes = new List<DocumentTreeNode>();
		AppendPage(nodes, page, pageTitle);
		return nodes;
	}

	private static void AppendPage(List<DocumentTreeNode> nodes, DocumentPage page, string title)
	{
		nodes.Add(new DocumentTreeNode
		{
			Depth = 0,
			Title = title,
			Subtitle = Truncate(page.Text),
			RawReference = ResolvePageRawReference(page),
		});
		AppendElements(nodes, page.Elements, depth: 1);
	}

	/// <summary>Resolves the Apple Vision node reference for a page, unwrapping the PDFKit page reference when the
	/// page came from <see cref="ApplePdfKitRenderingExtractionClient"/>.</summary>
	private static AppleVisionDocumentNodeReference? ResolvePageRawReference(DocumentPage page) =>
		page.RawRepresentation switch
		{
			AppleVisionDocumentNodeReference direct => direct,
			ApplePdfKitPageReference pdfPage => pdfPage.InnerRawRepresentation as AppleVisionDocumentNodeReference,
			_ => null,
		};

	private static void AppendElements(List<DocumentTreeNode> nodes, IReadOnlyList<DocumentElement> elements, int depth)
	{
		foreach (var element in elements)
		{
			AppendElement(nodes, element, depth);
		}
	}

	private static void AppendElement(List<DocumentTreeNode> nodes, DocumentElement element, int depth)
	{
		var raw = element.RawRepresentation as AppleVisionDocumentNodeReference;
		switch (element)
		{
			case DocumentTable table:
				nodes.Add(new DocumentTreeNode
				{
					Depth = depth,
					Title = $"Table ({table.RowCount}x{table.ColumnCount})",
					Subtitle = table.Cells is null ? Truncate(table.MarkdownRepresentation) : null,
					RawReference = raw,
				});
				if (table.Cells is { Count: > 0 } cells)
				{
					foreach (var cell in cells)
					{
						AppendCell(nodes, cell, depth + 1);
					}
				}
				break;

			case AppleListElement list:
				nodes.Add(new DocumentTreeNode
				{
					Depth = depth,
					Title = "List",
					Subtitle = $"{list.Items.Count} item(s)",
					RawReference = raw,
				});
				foreach (var item in list.Items)
				{
					AppendElement(nodes, item, depth + 1);
				}
				break;

			case AppleListItemElement item:
				nodes.Add(new DocumentTreeNode
				{
					Depth = depth,
					Title = string.IsNullOrEmpty(item.MarkerString) ? "List item" : $"List item {item.MarkerString}",
					Subtitle = Truncate(item.Text),
					RawReference = raw,
				});
				if (item.Elements.Count > 0)
				{
					AppendElements(nodes, item.Elements, depth + 1);
				}
				break;

			case AppleBarcodeElement barcode:
				nodes.Add(new DocumentTreeNode
				{
					Depth = depth,
					Title = $"Barcode ({barcode.Symbology})",
					Subtitle = barcode.PayloadString is { Length: > 0 } payload ? Truncate(payload) : "(no text payload)",
					RawReference = raw,
				});
				break;

			case DocumentImage image:
				nodes.Add(new DocumentTreeNode
				{
					Depth = depth,
					Title = "Image",
					Subtitle = image.Caption ?? (image.Content is not null ? "(embedded image content)" : "(no content)"),
					RawReference = raw,
				});
				break;

			case DocumentBlock block:
				nodes.Add(new DocumentTreeNode
				{
					Depth = depth,
					Title = block.Kind?.Value ?? "Block",
					Subtitle = Truncate(block.Text),
					RawReference = raw,
				});
				break;

			default:
				nodes.Add(new DocumentTreeNode
				{
					Depth = depth,
					Title = element.GetType().Name,
					RawReference = raw,
				});
				break;
		}
	}

	private static void AppendCell(List<DocumentTreeNode> nodes, DocumentTableCell cell, int depth)
	{
		var kindText = cell.Kind is { } kind ? $" ({kind.Value})" : string.Empty;
		var span = cell.RowSpan > 1 || cell.ColumnSpan > 1 ? $" [span {cell.RowSpan}x{cell.ColumnSpan}]" : string.Empty;
		nodes.Add(new DocumentTreeNode
		{
			Depth = depth,
			Title = $"Cell[{cell.RowIndex},{cell.ColumnIndex}]{kindText}{span}",
			Subtitle = Truncate(cell.Content),
			RawReference = cell.RawRepresentation as AppleVisionDocumentNodeReference,
		});
		if (cell.Elements is { Count: > 0 } cellElements)
		{
			AppendElements(nodes, cellElements, depth + 1);
		}
	}

	private static string? Truncate(string? text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return null;
		}

		var singleLine = text.Replace('\n', ' ').Replace('\r', ' ');
		return singleLine.Length <= MaxPreviewLength
			? singleLine
			: string.Create(MaxPreviewLength + 1, singleLine, static (span, source) =>
			{
				source.AsSpan(0, MaxPreviewLength).CopyTo(span);
				span[MaxPreviewLength] = '\u2026';
			});
	}
}
