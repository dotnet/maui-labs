using System.Text.Json;
using System.Text;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;

namespace Microsoft.Maui.Essentials.AI;

internal static class AppleVisionRecognizeDocumentsMapper
{
	private const int MaximumNodes = 20_000;
	private const int MaximumDepth = 64;

	internal static DocumentExtractionPage ToPage(JsonElement snapshot, int pageNumber)
	{
		if (snapshot.ValueKind != JsonValueKind.Array)
			throw new InvalidDataException("Vision returned an invalid document snapshot.");

		var elements = new List<DocumentExtractionObservation>();
		var index = 0;
		foreach (var observation in snapshot.EnumerateArray())
		{
			var prefix = $"page-{pageNumber}/snapshot-{index++}";
			foreach (var node in ReadObservation(observation))
				elements.Add(BuildElement(node, prefix, elements.Count));
		}
		return new DocumentExtractionPage(pageNumber, elements);
	}

	private static IReadOnlyList<Node> ReadObservation(JsonElement observation)
	{
		if (!observation.TryGetProperty("nodes", out var values) ||
			values.ValueKind != JsonValueKind.Array || values.GetArrayLength() > MaximumNodes)
			throw new InvalidDataException("Vision returned invalid document nodes.");

		var nodes = values.EnumerateArray().Select((value, index) => new Node(value, index)).ToArray();
		var byPath = new Dictionary<string, Node>(StringComparer.Ordinal);
		foreach (var node in nodes)
			if (!byPath.TryAdd(node.Path, node))
				throw new InvalidDataException("Vision returned duplicate document paths.");

		foreach (var node in nodes)
		{
			var visited = new HashSet<string>(StringComparer.Ordinal) { node.Path };
			var current = node;
			for (var depth = 0; current.ParentPath is { } parentPath; depth++)
			{
				if (depth >= MaximumDepth || !visited.Add(parentPath) || !byPath.TryGetValue(parentPath, out var parent))
					throw new InvalidDataException("Vision returned an invalid document node hierarchy.");
				current = parent;
			}
			if (node.ParentPath is { } path)
				byPath[path].Children.Add(node);
			if (node.Kind == "tableCell" && (node.ParentPath is null || byPath[node.ParentPath].Kind != "table") ||
				node.Kind == "listItem" && (node.ParentPath is null || byPath[node.ParentPath].Kind != "list"))
				throw new InvalidDataException("Vision returned an invalid document node parent.");
		}

		var roots = nodes.Where(node => node.ParentPath is null).ToArray();
		foreach (var table in nodes.Where(node => node.Kind == "table"))
			ValidateTable(table);

		// Vision supplies paragraph order. Replace paragraphs inside titles/tables, without spatially sorting columns.
		var structured = roots.Where(node => node.Kind == "table" && node.Rows > 0 ||
			node.Kind == "title" && !string.IsNullOrWhiteSpace(node.Text)).ToArray();
		var emitted = new HashSet<string>(StringComparer.Ordinal);
		var result = new List<Node>();
		foreach (var paragraph in roots.Where(node => node.Kind == "paragraph" && !string.IsNullOrWhiteSpace(node.Text)))
		{
			var match = structured.FirstOrDefault(candidate => candidate.Kind == "title"
				? paragraph.Text == candidate.Text && Overlap(paragraph, candidate) >= 0.7
				: Overlap(paragraph, candidate) >= 0.7);
			var selected = match ?? paragraph;
			if (emitted.Add(selected.Path))
				result.Add(selected);
		}
		foreach (var candidate in structured)
			if (emitted.Add(candidate.Path))
				result.Add(candidate);
		return result;
	}

	private static DocumentExtractionObservation BuildElement(Node node, string prefix, int order)
	{
		var id = $"{prefix}/node-{node.Index}";
		if (node.Kind != "table")
			return new DocumentExtractionText(id, order,
				node.Kind == "title" ? DocumentTextKind.Header : DocumentTextKind.Paragraph, node.Text, node.Text);

		var cells = node.Children.Where(child => child.Kind == "tableCell" && child.Text.Length > 0).Select((child, index) =>
			new DocumentExtractionTableCell(child.Row, child.Column,
				new DocumentExtractionText($"{prefix}/node-{child.Index}", index,
					DocumentTextKind.Paragraph, child.Text, child.Text))).ToArray();
		var grid = new string?[node.Rows, node.Columns];
		foreach (var cell in cells)
			grid[cell.Row, cell.Column] = cell.Content.Markdown;
		var markdown = new StringBuilder();
		for (var row = 0; row < node.Rows; row++)
		{
			markdown.Append('|');
			for (var column = 0; column < node.Columns; column++)
				markdown.Append(' ').Append((grid[row, column] ?? "").Replace("|", "\\|")
					.Replace("\r", " ").Replace("\n", " ")).Append(" |");
			markdown.AppendLine();
			if (row == 0)
			{
				markdown.Append('|');
				for (var column = 0; column < node.Columns; column++)
					markdown.Append(" --- |");
				markdown.AppendLine();
			}
		}
		return new DocumentExtractionTable(id, order, markdown.ToString().TrimEnd(), node.Rows, node.Columns, cells);
	}

	private static void ValidateTable(Node table)
	{
		var cells = table.Children.Where(child => child.Kind == "tableCell").Select(child =>
		{
			var row = child.Json.GetProperty("rowIndex").GetInt32();
			var column = child.Json.GetProperty("columnIndex").GetInt32();
			var rowSpan = child.Json.GetProperty("rowSpan").GetInt32();
			var columnSpan = child.Json.GetProperty("columnSpan").GetInt32();
			if (row < 0 || column < 0 || rowSpan < 1 || columnSpan < 1)
				throw new InvalidDataException("Vision returned an invalid table cell.");
			child.Row = row;
			child.Column = column;
			return (Row: row, Column: column, RowSpan: rowSpan, ColumnSpan: columnSpan);
		}).ToArray();
		if (cells.Length == 0)
			return;

		var rows = cells.Max(cell => (long)cell.Row + cell.RowSpan);
		var columns = cells.Max(cell => (long)cell.Column + cell.ColumnSpan);
		if (rows > MaximumNodes || columns > MaximumNodes || rows * columns > MaximumNodes)
			throw new InvalidDataException("Vision returned invalid table dimensions.");

		var occupied = new bool[(int)rows, (int)columns];
		foreach (var cell in cells)
			for (var row = cell.Row; row < cell.Row + cell.RowSpan; row++)
				for (var column = cell.Column; column < cell.Column + cell.ColumnSpan; column++)
				{
					if (occupied[row, column])
						throw new InvalidDataException("Vision returned overlapping table cells.");
					occupied[row, column] = true;
				}
		table.Rows = (int)rows;
		table.Columns = (int)columns;
	}

	private static double Overlap(Node first, Node second)
	{
		if (first.Bounds is not { } a || second.Bounds is not { } b)
			return 0;
		var area = (a.Right - a.Left) * (a.Top - a.Bottom);
		return area <= 0 ? 0 : Math.Max(0, Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left)) *
			Math.Max(0, Math.Min(a.Top, b.Top) - Math.Max(a.Bottom, b.Bottom)) / area;
	}

	private static string? GetString(JsonElement value, string name) =>
		value.TryGetProperty(name, out var property) && property.ValueKind != JsonValueKind.Null ? property.GetString() : null;

	private sealed class Node
	{
		internal Node(JsonElement json, int index)
		{
			Json = json;
			Index = index;
			Path = GetString(json, "path") ?? throw new InvalidDataException("Vision returned a node without a path.");
			ParentPath = GetString(json, "parentPath");
			Kind = GetString(json, "kind") ?? "";
			Text = GetString(json, "text") ?? "";
			if (json.TryGetProperty("confidence", out var confidence) && confidence.ValueKind != JsonValueKind.Null)
			{
				var value = confidence.GetDouble();
				if (!double.IsFinite(value) || value is < 0 or > 1)
					throw new InvalidDataException("Vision returned invalid confidence.");
			}
			if (!json.TryGetProperty("polygon", out var polygon))
				return;
			var coordinates = polygon.EnumerateArray().Select(value => value.GetDouble()).ToArray();
			if (coordinates.Length == 0)
				return;
			if (coordinates.Length < 6 || coordinates.Length % 2 != 0 ||
				coordinates.Any(value => !double.IsFinite(value) || !float.IsFinite((float)value)))
				throw new InvalidDataException("Vision returned an invalid normalized polygon.");
			var xs = coordinates.Where((_, index) => index % 2 == 0).ToArray();
			var ys = coordinates.Where((_, index) => index % 2 == 1).ToArray();
			Bounds = (xs.Min(), ys.Min(), xs.Max(), ys.Max());
		}

		internal JsonElement Json { get; }
		internal int Index { get; }
		internal string Path { get; }
		internal string? ParentPath { get; }
		internal string Kind { get; }
		internal string Text { get; }
		internal (double Left, double Bottom, double Right, double Top)? Bounds { get; }
		internal List<Node> Children { get; } = [];
		internal int Row { get; set; }
		internal int Column { get; set; }
		internal int Rows { get; set; }
		internal int Columns { get; set; }
	}
}
