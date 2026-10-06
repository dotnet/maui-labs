using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DataIngestion;

namespace Microsoft.Maui.Essentials.AI;

internal static class AppleVisionIngestionMapper
{
	private const int MaximumNodes = 20_000;

	internal static IngestionDocumentSection MapPage(JsonElement observations, int pageNumber)
	{
		if (observations.ValueKind != JsonValueKind.Array)
			throw new InvalidDataException("Vision returned an invalid document snapshot.");

		var section = new IngestionDocumentSection { PageNumber = pageNumber };
		foreach (var observation in observations.EnumerateArray())
		{
			var nodes = observation.GetProperty("nodes").EnumerateArray().Select(value => new Node(value)).ToArray();
			if (nodes.Length > MaximumNodes)
				throw new InvalidDataException("Vision returned too many document nodes.");
			var byPath = nodes.ToDictionary(node => node.Path, StringComparer.Ordinal);
			var candidates = new List<(Node Node, IngestionDocumentElement Element)>();
			foreach (var node in nodes)
			{
				if (node.ParentPath is null && node.Kind == "title" && !string.IsNullOrWhiteSpace(node.Text))
					candidates.Add((node, new IngestionDocumentHeader(node.Text)
						{ Level = 1, Text = node.Text, PageNumber = pageNumber }));
				else if (node.ParentPath is null && node.Kind == "table")
					candidates.Add((node, ReadTable(node, nodes, pageNumber)));
				else if (node.Kind == "listItem" && IsWithinRootList(node, byPath) &&
					!string.IsNullOrWhiteSpace(node.Text) && !candidates.Any(candidate =>
						candidate.Node.Kind == "listItem" && candidate.Node.Text == node.Text &&
						Overlap(candidate.Node, node) >= 0.85 && Overlap(node, candidate.Node) >= 0.85))
					candidates.Add((node, Paragraph(node.Text, pageNumber)));
			}

			// Vision's paragraph sequence preserves columns; replace overlapping structured regions in that order.
			var emitted = new HashSet<string>(StringComparer.Ordinal);
			foreach (var paragraph in nodes.Where(node => node.ParentPath is null && node.Kind == "paragraph"))
			{
				var match = candidates.FindIndex(candidate => candidate.Node.Kind switch
				{
					"title" => paragraph.Text == candidate.Node.Text && Overlap(paragraph, candidate.Node) >= 0.7,
					"table" => Overlap(paragraph, candidate.Node) >= 0.7,
					_ => Overlap(paragraph, candidate.Node) >= 0.55 &&
						paragraph.Text.Contains(candidate.Node.Text, StringComparison.OrdinalIgnoreCase),
				});
				if (match >= 0)
				{
					var candidate = candidates[match];
					if (emitted.Add(candidate.Node.Path))
						section.Elements.Add(candidate.Element);
				}
				else if (!string.IsNullOrWhiteSpace(paragraph.Text))
					section.Elements.Add(Paragraph(paragraph.Text, pageNumber));
			}
			foreach (var candidate in candidates)
				if (emitted.Add(candidate.Node.Path))
					section.Elements.Add(candidate.Element);
		}
		return section;
	}

	private static IngestionDocumentParagraph Paragraph(string text, int pageNumber) =>
		new(text) { Text = text, PageNumber = pageNumber };

	private static bool IsWithinRootList(Node node, IReadOnlyDictionary<string, Node> nodes)
	{
		var visited = new HashSet<string>(StringComparer.Ordinal);
		for (var depth = 0; node.ParentPath is { } parent && depth < 64; depth++)
		{
			if (!visited.Add(parent) || !nodes.TryGetValue(parent, out var ancestor))
				throw new InvalidDataException("Vision returned an invalid document node hierarchy.");
			node = ancestor;
			if (node.ParentPath is null)
				return node.Kind == "list";
		}
		return false;
	}

	private static IngestionDocumentTable ReadTable(Node table, Node[] nodes, int pageNumber)
	{
		var values = nodes.Where(node => node.ParentPath == table.Path && node.Kind == "tableCell")
			.Select(node => (Node: node, Row: node.Json.GetProperty("rowIndex").GetInt32(),
				Column: node.Json.GetProperty("columnIndex").GetInt32(),
				RowSpan: node.Json.GetProperty("rowSpan").GetInt32(),
				ColumnSpan: node.Json.GetProperty("columnSpan").GetInt32())).ToArray();
		long rows = 0, columns = 0;
		foreach (var cell in values)
		{
			if (cell.Row < 0 || cell.Column < 0 || cell.RowSpan < 1 || cell.ColumnSpan < 1)
				throw new InvalidDataException("Vision returned an invalid table cell.");
			rows = Math.Max(rows, (long)cell.Row + cell.RowSpan);
			columns = Math.Max(columns, (long)cell.Column + cell.ColumnSpan);
		}
		if (rows > MaximumNodes || columns > MaximumNodes || rows * columns > MaximumNodes)
			throw new InvalidDataException("Vision returned invalid table dimensions.");

		var cells = new IngestionDocumentElement?[(int)rows, (int)columns];
		var occupied = new bool[(int)rows, (int)columns];
		foreach (var cell in values)
		{
			for (var row = cell.Row; row < cell.Row + cell.RowSpan; row++)
				for (var column = cell.Column; column < cell.Column + cell.ColumnSpan; column++)
				{
					if (occupied[row, column])
						throw new InvalidDataException("Vision returned overlapping table cells.");
					occupied[row, column] = true;
				}
			if (!string.IsNullOrWhiteSpace(cell.Node.Text))
				cells[cell.Row, cell.Column] = Paragraph(cell.Node.Text, pageNumber);
		}

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

	private static double Overlap(Node first, Node second)
	{
		if (first.Bounds is not { } a || second.Bounds is not { } b)
			return 0;
		var area = (a.Right - a.Left) * (a.Top - a.Bottom);
		return area <= 0 ? 0 : Math.Max(0, Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left)) *
			Math.Max(0, Math.Min(a.Top, b.Top) - Math.Max(a.Bottom, b.Bottom)) / area;
	}

	private sealed class Node
	{
		internal Node(JsonElement json)
		{
			Json = json;
			Path = json.GetProperty("path").GetString() ?? throw new InvalidDataException("Vision returned a node without a path.");
			Kind = json.GetProperty("kind").GetString() ?? "";
			ParentPath = json.TryGetProperty("parentPath", out var parent) ? parent.GetString() : null;
			Text = json.TryGetProperty("text", out var text) ? text.GetString() ?? "" : "";
			if (json.TryGetProperty("polygon", out var polygon))
			{
				var points = polygon.EnumerateArray().Select(value => value.GetDouble()).ToArray();
				if (points.Length > 0)
				{
					if (points.Length % 2 != 0 || points.Any(value => !double.IsFinite(value)))
						throw new InvalidDataException("Vision returned an invalid document polygon.");
					var xs = points.Where((_, index) => index % 2 == 0).ToArray();
					var ys = points.Where((_, index) => index % 2 == 1).ToArray();
					Bounds = (xs.Min(), ys.Min(), xs.Max(), ys.Max());
				}
			}
		}

		internal JsonElement Json { get; }
		internal string Path { get; }
		internal string Kind { get; }
		internal string? ParentPath { get; }
		internal string Text { get; }
		internal (double Left, double Bottom, double Right, double Top)? Bounds { get; }
	}
}
