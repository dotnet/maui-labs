using System.Text.Json;
using EssentialsAISample.Services;
using Microsoft.Maui.Essentials.AI.UnitTests.TestHelpers;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.UnitTests;

public partial class JsonStreamChunkerTests
{
	/// <summary>
	/// Integration tests using real JSONL test data files.
	/// </summary>
	public class IntegrationTests
	{
		[Theory]
		[InlineData(false)]
		[InlineData(true)]
		public void Process_RecordedLongString_UpdatesModelBeforeFinalSnapshot(bool nested)
		{
			var fileName = nested ? "apple-nested-long-string-os27.jsonl" : "apple-long-string-os27.jsonl";
			var lines = DataStreamsHelper.GetFileLines(Path.Combine("Apple", fileName));
			using var expected = JsonDocument.Parse(lines[^1]);
			var finalText = nested
				? expected.RootElement.GetProperty("sections")[0].GetProperty("text").GetString()!
				: expected.RootElement.GetProperty("message").GetString()!;
			var chunker = new JsonStreamChunker();
			var deserializer = new StreamingJsonDeserializer<Dictionary<string, JsonElement>>();
			var partialLengths = new HashSet<int>();

			// Replay real native snapshots through the same partial-model path used by the sample.
			foreach (var line in lines[..^1])
			{
				var model = deserializer.ProcessChunk(chunker.Process(line));
				if (ReadText(model, nested) is { Length: > 0 } partial && partial.Length < finalText.Length)
				{
					Assert.StartsWith(partial, finalText, StringComparison.Ordinal);
					partialLengths.Add(partial.Length);
				}
			}

			Assert.True(partialLengths.Count >= 3, "A long native string must produce multiple partial model updates.");
			Assert.True(partialLengths.Max() >= finalText.Length / 2, "Streaming must continue as the string grows.");

			deserializer.ProcessChunk(chunker.Process(lines[^1]));
			var finalModel = deserializer.ProcessChunk(chunker.Flush());

			Assert.NotNull(finalModel);
			Assert.Equal(finalText, ReadText(finalModel, nested));
			using var actual = JsonDocument.Parse(deserializer.PartialJson);
			Assert.True(JsonElementsAreEqual(expected.RootElement, actual.RootElement));
		}

		private static string? ReadText(Dictionary<string, JsonElement>? model, bool nested)
		{
			if (model is null)
				return null;

			if (!nested)
				return model.TryGetValue("message", out var message) && message.ValueKind == JsonValueKind.String
					? message.GetString()
					: null;

			return model.TryGetValue("sections", out var sections) &&
				sections.ValueKind == JsonValueKind.Array && sections.GetArrayLength() > 0 &&
				sections[0].TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String
					? text.GetString()
					: null;
		}

		[Theory]
		[MemberData(nameof(DataStreamsHelper.JsonlStreams), MemberType = typeof(DataStreamsHelper))]
		public void Process_FromJsonlFile_ProducesValidJsonMatchingFinalLine(string fileName, string filePath)
		{
			_ = fileName; // We are not using the parameter here

			// Arrange
			var chunker = new JsonStreamChunker();
			var lines = File.ReadAllLines(filePath);

			// Act
			var chunks = new List<string>();
			foreach (var line in lines)
				chunks.Add(chunker.Process(line));
			chunks.Add(chunker.Flush());

			var concatenated = string.Concat(chunks);
			var finalLine = lines[^1];

			// Assert
			// 1. Concatenated chunks should be valid JSON
			JsonDocument concatenatedDoc;
			try
			{
				concatenatedDoc = JsonDocument.Parse(concatenated);
			}
			catch (JsonException ex)
			{
				Assert.Fail($"Concatenated chunks are not valid JSON: {ex.Message}\n\nConcatenated:\n{concatenated}");
				return;
			}

			// 2. Should match the final line when parsed
			var finalDoc = JsonDocument.Parse(finalLine);

			// Deep equality check
			var areEqual = JsonElementsAreEqual(finalDoc.RootElement, concatenatedDoc.RootElement);
			Assert.True(areEqual,
				$"Concatenated JSON does not match final line.\n\nExpected:\n{finalLine}\n\nActual:\n{concatenated}");
		}
	}
}
