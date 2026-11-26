using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Maui.Essentials.AI;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, PropertyNameCaseInsensitive = false)]
[JsonSerializable(typeof(AppleVisionRecognizeDocumentsSnapshot.Observation[]))]
internal partial class AppleVisionRecognizeDocumentsSnapshotJsonContext : JsonSerializerContext;

internal static class AppleVisionRecognizeDocumentsSnapshot
{
	private const int MaximumNodes = 20_000;

	internal static Observation[] Deserialize(JsonElement snapshot)
	{
		if (snapshot.ValueKind != JsonValueKind.Array)
			throw new InvalidDataException("Vision returned an invalid document snapshot.");

		var rawObservations = snapshot.EnumerateArray().ToArray();
		foreach (var rawObservation in rawObservations)
			if (rawObservation.ValueKind != JsonValueKind.Object ||
				!rawObservation.TryGetProperty("nodes", out var rawNodes) ||
				rawNodes.ValueKind != JsonValueKind.Array ||
				rawNodes.GetArrayLength() > MaximumNodes)
				throw new InvalidDataException("Vision returned invalid document nodes.");

		try
		{
			var observations = snapshot.Deserialize(AppleVisionRecognizeDocumentsSnapshotJsonContext.Default.ObservationArray)
				?? throw new InvalidDataException("Vision returned an invalid document snapshot.");
			if (observations.Length != rawObservations.Length)
				throw new InvalidDataException("Vision returned an invalid document snapshot.");

			for (var observationIndex = 0; observationIndex < observations.Length; observationIndex++)
			{
				var observation = observations[observationIndex];
				var rawObservation = rawObservations[observationIndex];
				if (observation is null ||
					rawObservation.ValueKind != JsonValueKind.Object ||
					!rawObservation.TryGetProperty("nodes", out var rawNodes) ||
					rawNodes.ValueKind != JsonValueKind.Array ||
					rawNodes.GetArrayLength() > MaximumNodes ||
					observation.Nodes is not { } nodes)
					throw new InvalidDataException("Vision returned invalid document nodes.");

				var rawNodeValues = rawNodes.EnumerateArray().ToArray();
				if (nodes.Count != rawNodeValues.Length)
					throw new InvalidDataException("Vision returned invalid document nodes.");

				observation.RawSnapshot = rawObservation.Clone();
				observation.HasPolygon = rawObservation.TryGetProperty("polygon", out _);
				for (var nodeIndex = 0; nodeIndex < nodes.Count; nodeIndex++)
				{
					var node = nodes[nodeIndex];
					var rawNode = rawNodeValues[nodeIndex];
					if (node is null || rawNode.ValueKind != JsonValueKind.Object)
						throw new InvalidDataException("Vision returned invalid document nodes.");

					node.RawSnapshot = rawNode.Clone();
					node.HasPolygon = rawNode.TryGetProperty("polygon", out _);
					if (node.Words is { } words)
					{
						if (!rawNode.TryGetProperty("words", out var rawWords) ||
							rawWords.ValueKind != JsonValueKind.Array ||
							rawWords.GetArrayLength() != words.Count)
							throw new InvalidDataException("Vision returned invalid document words.");

						var rawWordValues = rawWords.EnumerateArray().ToArray();
						for (var wordIndex = 0; wordIndex < words.Count; wordIndex++)
						{
							var word = words[wordIndex];
							var rawWord = rawWordValues[wordIndex];
							if (word is null || rawWord.ValueKind != JsonValueKind.Object)
								throw new InvalidDataException("Vision returned invalid document words.");

							word.RawSnapshot = rawWord.Clone();
							word.HasPolygon = rawWord.TryGetProperty("polygon", out _);
						}
					}
				}
			}

			return observations;
		}
		catch (JsonException exception)
		{
			throw new InvalidDataException("Vision returned an invalid document snapshot.", exception);
		}
	}

	internal sealed class Observation
	{
		public string? Uuid { get; set; }
		public double? Confidence { get; set; }
		public string? Transcript { get; set; }
		public double[]? Polygon { get; set; }
		public bool StructureTruncated { get; set; }
		public int ProjectedNodeCount { get; set; }
		public int MaximumTraversalDepth { get; set; }
		public int RepeatedContainerCount { get; set; }
		public string? FirstRepeatedContainerPath { get; set; }
		public string? FirstRepeatedAncestorPath { get; set; }
		public List<Node>? Nodes { get; set; }

		[JsonIgnore]
		internal JsonElement RawSnapshot { get; set; }

		[JsonIgnore]
		internal bool HasPolygon { get; set; }
	}

	internal sealed class Node
	{
		public string? Kind { get; set; }
		public string? Path { get; set; }
		public string? ParentPath { get; set; }
		public string? Text { get; set; }
		public double[]? Polygon { get; set; }
		public double? Confidence { get; set; }
		public int? RowIndex { get; set; }
		public int? ColumnIndex { get; set; }
		public int? RowSpan { get; set; }
		public int? ColumnSpan { get; set; }
		public string? ItemString { get; set; }
		public string? MarkerString { get; set; }
		public string? MarkerType { get; set; }
		public string? Symbology { get; set; }
		public string? PayloadString { get; set; }
		public string? PayloadDataBase64 { get; set; }
		public bool? IsGS1DataCarrier { get; set; }
		public bool? IsColorInverted { get; set; }
		public string? SupplementalPayloadString { get; set; }
		public string? SupplementalPayloadDataBase64 { get; set; }
		public string? SupplementalCompositeType { get; set; }
		public string? TextAlignment { get; set; }
		public string[]? RecognitionLanguages { get; set; }
		public JsonElement DetectedData { get; set; }
		public JsonElement Candidates { get; set; }
		public List<Word>? Words { get; set; }

		[JsonIgnore]
		internal JsonElement RawSnapshot { get; set; }

		[JsonIgnore]
		internal bool HasPolygon { get; set; }
	}

	internal sealed class Word
	{
		public double? Confidence { get; set; }
		public double[]? Polygon { get; set; }
		public string[]? RecognitionLanguages { get; set; }
		public JsonElement Candidates { get; set; }

		[JsonIgnore]
		internal JsonElement RawSnapshot { get; set; }

		[JsonIgnore]
		internal bool HasPolygon { get; set; }
	}
}
