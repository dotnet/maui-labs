using System.Text.Json;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;

namespace Microsoft.Maui.Essentials.AI;

internal static class AppleVisionRecognizeDocumentsMapper
{
	private const int MaximumNodes = 20_000;
	private const int MaximumDepth = 64;

	internal static DocumentExtractionPage ToPage(
		JsonElement snapshot, int pageNumber, string identifier, bool captureRawOutput = true)
	{
		if (snapshot.ValueKind != JsonValueKind.Array)
			throw new InvalidDataException("Vision returned an invalid document snapshot.");
		var output = new DocumentSourceOutput($"{identifier}/vision/page/{pageNumber}",
			rawRepresentation: captureRawOutput ? snapshot.Clone() : null,
			captureDisabled: !captureRawOutput, providerName: "apple.vision.native-bridge-snapshot");
		var frame = new DocumentSourcePage($"{identifier}/page/{pageNumber}", pageNumber,
			new(1, 1), DocumentCoordinateUnit.Normalized, DocumentCoordinateOrigin.BottomLeft, output);
		var elements = new List<DocumentExtractionObservation>();
		var annotations = new List<DocumentAnnotation>();
		var observationIds = new HashSet<string>(StringComparer.Ordinal);
		var observationIndex = 0;
		foreach (var observation in snapshot.EnumerateArray())
		{
			var identity = GetString(observation, "uuid") ?? $"snapshot-observation-{observationIndex}";
			observationIndex++;
			if (!observationIds.Add(identity))
				throw new InvalidDataException("Vision returned duplicate observation identities.");
			var transcript = GetString(observation, "transcript");
			if (transcript is not null)
			{
				annotations.Add(new("apple.vision.transcript", transcript,
					regions: Region(observation, frame) is { } observationRegion ? [observationRegion] : null,
					confidence: Confidence(observation), sourceOutput: output,
					rawRepresentation: captureRawOutput ? observation.Clone() : null));
			}
			var nodes = ReadNodes(observation, frame);
			var roots = nodes.Where(node => node.ParentPath is null).ToArray();
			var selected = SelectContent(roots);
			var firstElement = elements.Count;
			foreach (var node in selected)
			{
				var element = BuildElement(node, $"{frame.Identifier}/observation/{identity}",
					elements.Count, output, captureRawOutput);
				if (element is not null)
					elements.Add(element);
			}
			if (elements.Count == firstElement && !string.IsNullOrEmpty(transcript))
				elements.Add(new DocumentExtractionText($"{frame.Identifier}/observation/{identity}/transcript",
					elements.Count, DocumentTextKind.Paragraph, transcript, transcript));
		}
		return new(pageNumber, elements,
			sourcePage: frame, annotations: annotations, sourceOutputs: [output]);
	}

	private static Node[] ReadNodes(JsonElement observation, DocumentSourcePage frame)
	{
		if (!observation.TryGetProperty("nodes", out var values) ||
			values.ValueKind != JsonValueKind.Array || values.GetArrayLength() > MaximumNodes)
			throw new InvalidDataException("Vision returned invalid document nodes.");
		var nodes = values.EnumerateArray().Select(value => new Node(value, frame)).ToArray();
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
		return nodes;
	}

	private static IReadOnlyList<Node> SelectContent(IReadOnlyList<Node> roots)
	{
		// Keep Apple's paragraph order; native structure may duplicate a paragraph projection.
		var structured = roots.Where(node => node.Kind is "title" or "table" or "list").ToArray();
		var result = new List<Node>();
		var emitted = new HashSet<string>(StringComparer.Ordinal);
		foreach (var paragraph in roots.Where(node => node.Kind == "paragraph"))
		{
			var selected = structured.FirstOrDefault(candidate => candidate.Kind switch
			{
				"title" => paragraph.Text == candidate.Text && Overlap(paragraph, candidate) >= 0.7,
				"list" => MatchListProjection(paragraph, candidate),
				_ => Overlap(paragraph, candidate) >= 0.7,
			}) ?? paragraph;
			if (!ReferenceEquals(selected, paragraph) && !selected.ParagraphProjections.Contains(paragraph))
				selected.ParagraphProjections.Add(paragraph);
			if (emitted.Add(selected.Path))
				result.Add(selected);
		}
		foreach (var node in roots)
			if (node.Kind != "paragraph" && emitted.Add(node.Path))
				result.Add(node);
		return result;
	}

	private static DocumentExtractionObservation? BuildElement(
		Node node, string prefix, int order, DocumentSourceOutput output, bool capture)
	{
		var id = $"{prefix}/node/{node.Path}";
		var annotations = Annotations(node, output, capture);
		switch (node.Kind)
		{
			case "title":
			case "paragraph":
				AddNestedAnnotations(node, annotations, output, capture);
				return new DocumentExtractionText(id, order,
					node.Kind == "title" ? DocumentTextKind.Header : DocumentTextKind.Paragraph,
					node.Text, text: node.Text, annotations: annotations);
			case "table":
				var children = node.Children.Where(child => child.Kind == "tableCell").ToArray();
				if (children.Length == 0)
					return null;
				var cells = new List<DocumentExtractionTableCell>();
				long rows = 0, columns = 0;
				foreach (var child in children)
				{
					var row = Integer(child.Json, "rowIndex");
					var column = Integer(child.Json, "columnIndex");
					var rowSpan = Integer(child.Json, "rowSpan");
					var columnSpan = Integer(child.Json, "columnSpan");
					if (row < 0 || column < 0 || rowSpan < 1 || columnSpan < 1)
						throw new InvalidDataException("Vision returned an invalid table cell.");
					rows = Math.Max(rows, (long)row + rowSpan);
					columns = Math.Max(columns, (long)column + columnSpan);
					var cellAnnotations = Annotations(child, output, capture);
					var nested = SelectContent(child.Children);
					DocumentExtractionObservation? content;
					if (nested.Count == 1)
					{
						content = BuildElement(nested[0], prefix, 0, output, capture);
						if (child.Text.Length > 0)
							cellAnnotations.Add(new("apple.vision.transcript", child.Text));
					}
					else
					{
						// The canonical cell accepts one observation. Preserve additional native semantics as typed annotations.
						content = child.Text.Length == 0 ? null : new DocumentExtractionText(
							$"{prefix}/node/{child.Path}/transcript", 0, DocumentTextKind.Paragraph, child.Text, child.Text);
						AddNestedAnnotations(child, cellAnnotations, output, capture);
					}
					cells.Add(new(row, column, content, rowSpan, columnSpan, annotations: cellAnnotations));
				}
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
				return new DocumentExtractionTable(id, order, "", (int)rows, (int)columns, cells, annotations: annotations);
			case "list":
			case "barcode":
				AddNestedAnnotations(node, annotations, output, capture);
				var text = node.Kind == "list" ? RenderList(node) :
					GetString(node.Json, "payloadString") ?? node.Text;
				return new DocumentExtractionExtension(id, order, $"apple.vision.{node.Kind}", text,
					text.Length == 0 ? null : text, annotations: annotations, value: SemanticValue(node));
			default:
				return null;
		}
	}

	private static bool MatchListProjection(Node paragraph, Node list)
	{
		var items = list.Children.Where(child => child.Kind == "listItem").ToArray();
		foreach (var item in items)
			if (MatchesItemText(paragraph.Text, item) &&
				(Overlap(paragraph, item) >= 0.7 || item.Region is null && Overlap(paragraph, list) >= 0.7))
			{
				list.ItemProjections.TryAdd(item.Path, paragraph.Text);
				return true;
			}
		var lines = paragraph.Text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
		if (items.Length > 0 && lines.Length == items.Length && Overlap(paragraph, list) >= 0.7 &&
			items.Select((item, index) => MatchesItemText(lines[index], item)).All(match => match))
		{
			list.WholeListProjection ??= paragraph;
			return true;
		}
		return false;
	}

	private static bool MatchesItemText(string text, Node item)
	{
		var content = GetString(item.Json, "itemString") ?? item.Text;
		if (text == content)
			return true;
		var marker = GetString(item.Json, "markerString");
		return !string.IsNullOrEmpty(marker) && text.StartsWith(marker, StringComparison.Ordinal) &&
			text[marker.Length..].TrimStart() == content;
	}

	private static string RenderList(Node list)
	{
		if (list.WholeListProjection is { } whole)
			return whole.Text;
		return string.Join("\n", list.Children.Where(child => child.Kind == "listItem").Select(item =>
		{
			var text = list.ItemProjections.GetValueOrDefault(item.Path) ?? GetString(item.Json, "itemString") ?? item.Text;
			var marker = GetString(item.Json, "markerString");
			if (!string.IsNullOrEmpty(marker) && !text.StartsWith(marker, StringComparison.Ordinal))
				text = marker + (char.IsWhiteSpace(marker[^1]) ? "" : " ") + text;
			foreach (var nested in SelectContent(item.Children))
			{
				// Vision can expose an item's own region again through its recursive content.
				if (nested.Kind == "paragraph" && MatchesItemText(nested.Text, item) && Overlap(nested, item) >= 0.85 ||
					IsRepeatedItemList(nested, item))
					continue;
				var childText = nested.Kind == "list" ? RenderList(nested) : nested.Kind == "paragraph" ? nested.Text : "";
				if (childText.Length == 0)
					continue;
				text += "\n" + childText;
			}
			return text;
		}));
	}

	private static bool IsRepeatedItemList(Node node, Node item) =>
		node.Kind == "list" && node.Children.Count > 0 && node.Children.All(child =>
			child.Kind == "listItem" && child.Children.Count == 0 &&
			child.Text == item.Text &&
			GetString(child.Json, "itemString") == GetString(item.Json, "itemString") &&
			GetString(child.Json, "markerString") == GetString(item.Json, "markerString") &&
			Overlap(child, item) >= 0.85);

	private static List<DocumentAnnotation> Annotations(Node node, DocumentSourceOutput output, bool capture)
	{
		var result = new List<DocumentAnnotation>
		{
			new($"apple.vision.{node.Kind}", value: SemanticValue(node),
				regions: node.Region is { } region ? [region] : null, confidence: node.Confidence,
				sourceOutput: output, rawRepresentation: capture ? node.Json.Clone() : null),
		};
		foreach (var projection in node.ParagraphProjections)
			result.AddRange(Annotations(projection, output, capture));
		foreach (var name in new[] { "textAlignment", "recognitionLanguages", "detectedData", "candidates" })
			if (node.Json.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null)
				result.Add(new($"apple.vision.{name}", value.Clone()));
		if (node.Json.TryGetProperty("words", out var words) && words.ValueKind == JsonValueKind.Array)
			foreach (var word in words.EnumerateArray())
			{
				var wordRegion = Region(word, node.Frame);
				result.Add(new("apple.vision.word",
					value: Fields(word, "text", "recognitionLanguages", "candidates"),
					regions: wordRegion is null ? null : [wordRegion], confidence: Confidence(word),
					sourceOutput: output, rawRepresentation: capture ? word.Clone() : null));
			}
		return result;
	}

	private static void AddNestedAnnotations(
		Node node, List<DocumentAnnotation> annotations, DocumentSourceOutput output, bool capture)
	{
		foreach (var child in node.Children)
		{
			annotations.AddRange(Annotations(child, output, capture));
			AddNestedAnnotations(child, annotations, output, capture);
		}
	}

	private static object? SemanticValue(Node node) => node.Kind switch
	{
		"list" => node.Children.Where(child => child.Kind == "listItem")
			.Select(child => Fields(child.Json, "itemString", "markerString", "markerType", "text")).ToArray(),
		"listItem" => Fields(node.Json, "itemString", "markerString", "markerType", "text"),
		"barcode" => Fields(node.Json, "symbology", "payloadString", "payloadDataBase64", "isGS1DataCarrier",
			"isColorInverted", "supplementalPayloadString", "supplementalPayloadDataBase64", "supplementalCompositeType"),
		"table" => node.Children.Where(child => child.Kind == "tableCell")
			.Select(child => Fields(child.Json, "rowIndex", "columnIndex", "rowSpan", "columnSpan", "text")).ToArray(),
		"tableCell" => node.Text,
		"title" or "paragraph" => node.Text,
		_ => null,
	};

	private static Dictionary<string, JsonElement> Fields(JsonElement source, params string[] names)
	{
		var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
		foreach (var name in names)
			if (source.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null)
				result.Add(name, value.Clone());
		return result;
	}

	private static DocumentConfidence? Confidence(JsonElement source)
	{
		if (!source.TryGetProperty("confidence", out var confidence) || confidence.ValueKind == JsonValueKind.Null)
			return null;
		if (!confidence.TryGetDouble(out var value) || !double.IsFinite(value) || value is < 0 or > 1)
			throw new InvalidDataException("Vision returned invalid confidence.");
		return new(value, "apple.vision.confidence", 0, 1);
	}

	private static DocumentRegion? Region(JsonElement source, DocumentSourcePage frame)
	{
		if (!source.TryGetProperty("polygon", out var polygon))
			return null;
		if (polygon.ValueKind != JsonValueKind.Array)
			throw new InvalidDataException("Vision returned an invalid polygon.");
		var coordinates = polygon.EnumerateArray().Select(value => value.GetDouble()).ToArray();
		if (coordinates.Length == 0)
			return null;
		if (coordinates.Length < 6 || coordinates.Length % 2 != 0 || coordinates.Any(value => !double.IsFinite(value)))
			throw new InvalidDataException("Vision returned an invalid normalized polygon.");
		return new(frame, Enumerable.Range(0, coordinates.Length / 2)
			.Select(index => new DocumentPoint(coordinates[index * 2], coordinates[index * 2 + 1])).ToArray());
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

	private static int Integer(JsonElement value, string name) =>
		value.TryGetProperty(name, out var number) && number.TryGetInt32(out var result) ? result :
			throw new InvalidDataException("Vision returned an invalid table cell.");

	private sealed class Node
	{
		internal Node(JsonElement json, DocumentSourcePage frame)
		{
			Json = json;
			Frame = frame;
			Path = GetString(json, "path") ?? throw new InvalidDataException("Vision returned a node without a path.");
			ParentPath = GetString(json, "parentPath");
			Kind = GetString(json, "kind") ?? "";
			Text = GetString(json, "text") ?? "";
			Confidence = AppleVisionRecognizeDocumentsMapper.Confidence(json);
			Region = AppleVisionRecognizeDocumentsMapper.Region(json, frame);
			if (Region is { } region)
				Bounds = (region.Polygon.Min(point => point.X), region.Polygon.Min(point => point.Y),
					region.Polygon.Max(point => point.X), region.Polygon.Max(point => point.Y));
		}
		internal JsonElement Json { get; }
		internal DocumentSourcePage Frame { get; }
		internal string Path { get; }
		internal string? ParentPath { get; }
		internal string Kind { get; }
		internal string Text { get; }
		internal DocumentConfidence? Confidence { get; }
		internal DocumentRegion? Region { get; }
		internal (double Left, double Bottom, double Right, double Top)? Bounds { get; }
		internal List<Node> Children { get; } = [];
		internal List<Node> ParagraphProjections { get; } = [];
		internal Dictionary<string, string> ItemProjections { get; } = new(StringComparer.Ordinal);
		internal Node? WholeListProjection { get; set; }
	}
}
