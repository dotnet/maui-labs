using System.Text.Json;
using EssentialsAISample.Services;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.UnitTests;

public partial class JsonStreamChunkerTests
{
	/// <summary>
	/// Tests for nested structures: arrays, objects, deeply nested paths.
	/// </summary>
	public class NestedTests
	{
		[Fact]
		public void Process_NestedEmptyArraysThenContent_ProducesValidJson()
		{
			// Arrange - simulates progressive array/object construction
			var chunker = new JsonStreamChunker();
			var lines = new[]
			{
				"""{"days": []}""",
				"""{"days": [{"activities": []}]}""",
				"""{"days": [{"activities": [{"description": "Hello"}]}]}"""
			};

			// Act
			var chunks = new List<string>();
			foreach (var line in lines)
				chunks.Add(chunker.Process(line));
			chunks.Add(chunker.Flush());

			var concatenated = string.Concat(chunks);

			// Assert - first check the concatenated output is parsable
			Assert.True(IsValidJson(concatenated), $"Invalid JSON produced:\n{concatenated}");

			var doc = JsonDocument.Parse(concatenated);
			var activity = doc.RootElement
				.GetProperty("days")[0]
				.GetProperty("activities")[0];
			Assert.Equal("Hello", activity.GetProperty("description").GetString());
		}

		[Fact]
		public void Process_NestedObject_HandlesCorrectly()
		{
			// Arrange
			var chunker = new JsonStreamChunker();
			var lines = new[]
			{
				"""{"days": [{"activities": []}]}""",
				"""{"days": [{"activities": [{"description": "Visit"}]}]}""",
				"""{"days": [{"activities": [{"description": "Visit the park"}]}]}"""
			};

			// Act
			var chunks = new List<string>();
			foreach (var line in lines)
				chunks.Add(chunker.Process(line));
			chunks.Add(chunker.Flush());
			var concatenated = string.Concat(chunks);

			// Assert
			var doc = JsonDocument.Parse(concatenated);
			var activity = doc.RootElement
				.GetProperty("days")[0]
				.GetProperty("activities")[0];
			Assert.Equal("Visit the park", activity.GetProperty("description").GetString());
		}

		[Fact]
		public void Process_ParentLevelChange_ClosesString()
		{
			// When a new array item appears, it should close strings in the previous item
			var chunker = new JsonStreamChunker();

			var chunks = new List<string>();
			chunks.Add(chunker.Process("""{"items": [{"name": "First"}]}"""));
			// New array item appears - should close "First"
			chunks.Add(chunker.Process("""{"items": [{"name": "First"}, {"name": "Second"}]}"""));
			chunks.Add(chunker.Flush());

			var concatenated = string.Concat(chunks);

			Assert.True(IsValidJson(concatenated), $"Invalid JSON: {concatenated}");
			var doc = JsonDocument.Parse(concatenated);
			Assert.Equal("First", doc.RootElement.GetProperty("items")[0].GetProperty("name").GetString());
			Assert.Equal("Second", doc.RootElement.GetProperty("items")[1].GetProperty("name").GetString());
		}

		[Fact]
		public void Process_ArrayOfStringsGrowsBesideOtherProperties_ProducesValidJson()
		{
			var chunker = new JsonStreamChunker();
			var lines = new[]
			{
				"""{"summary":"","category":"","keyPoints":[],"sentiment":""}""",
				"""{"summary":"Hello! How can I assist you today?","category":"greeting","keyPoints":["Hello!"],"sentiment":"positive"}""",
				"""{"summary":"Hello! How can I assist you today?","category":"greeting","keyPoints":["Hello!","assistance"],"sentiment":"positive"}"""
			};

			var chunks = new List<string>();
			foreach (var line in lines)
				chunks.Add(chunker.Process(line));
			chunks.Add(chunker.Flush());
			var concatenated = string.Concat(chunks);

			Assert.True(IsValidJson(concatenated),
				$"Invalid JSON: {concatenated}\n\nChunks:\n[{string.Join("], [", chunks)}]");
			var doc = JsonDocument.Parse(concatenated);
			var keyPoints = doc.RootElement.GetProperty("keyPoints");
			Assert.Equal("Hello!", keyPoints[0].GetString());
			Assert.Equal("assistance", keyPoints[1].GetString());
		}

		[Fact]
		public void Flush_MultiplePendingContainersGrowTogether_PreservesFinalContents()
		{
			var chunker = new JsonStreamChunker();
			var chunks = new List<string>
			{
				chunker.Process("""{"a":[],"b":[]}"""),
				chunker.Process("""{"a":["A"],"b":["B"]}"""),
				chunker.Flush()
			};

			var concatenated = string.Concat(chunks);

			Assert.True(IsValidJson(concatenated),
				$"Invalid JSON: {concatenated}\n\nChunks:\n[{string.Join("], [", chunks)}]");
			var doc = JsonDocument.Parse(concatenated);
			Assert.Equal("A", doc.RootElement.GetProperty("a")[0].GetString());
			Assert.Equal("B", doc.RootElement.GetProperty("b")[0].GetString());
		}

		[Fact]
		public void Process_OpenNestedStringAndPendingArrays_ProducesValidJson()
		{
			var chunker = new JsonStreamChunker();
			var lines = new[]
			{
				"""{"left":{"text":"a"}}""",
				"""{"left":{"text":"ab"},"right":["x","y"],"other":[]}""",
				"""{"left":{"text":"abc"},"right":["x","y"],"other":[1]}"""
			};

			var chunks = new List<string>();
			foreach (var line in lines)
				chunks.Add(chunker.Process(line));
			chunks.Add(chunker.Flush());
			var concatenated = string.Concat(chunks);

			Assert.True(IsValidJson(concatenated),
				$"Invalid JSON: {concatenated}\n\nChunks:\n[{string.Join("], [", chunks)}]");
			var doc = JsonDocument.Parse(concatenated);
			Assert.Equal("abc", doc.RootElement.GetProperty("left").GetProperty("text").GetString());
			Assert.Equal("x", doc.RootElement.GetProperty("right")[0].GetString());
			Assert.Equal("y", doc.RootElement.GetProperty("right")[1].GetString());
			Assert.Equal(1, doc.RootElement.GetProperty("other")[0].GetInt32());
		}

		[Fact]
		public void Process_PendingArrayStringGrowsInPlaceThenAddsItem_ProducesValidJson()
		{
			var chunker = new JsonStreamChunker();
			var lines = new[]
			{
				"""{"title":"","tags":[]}""",
				"""{"title":"Trip","tags":["su"]}""",
				"""{"title":"Trip p","tags":["sum"]}""",
				"""{"title":"Trip plan","tags":["summer"]}""",
				"""{"title":"Trip plan","tags":["summer","beach"]}"""
			};

			var chunks = new List<string>();
			foreach (var line in lines)
				chunks.Add(chunker.Process(line));
			chunks.Add(chunker.Flush());
			var concatenated = string.Concat(chunks);

			Assert.True(IsValidJson(concatenated),
				$"Invalid JSON: {concatenated}\n\nChunks:\n[{string.Join("], [", chunks)}]");
			var doc = JsonDocument.Parse(concatenated);
			Assert.Equal("Trip plan", doc.RootElement.GetProperty("title").GetString());
			Assert.Equal("summer", doc.RootElement.GetProperty("tags")[0].GetString());
			Assert.Equal("beach", doc.RootElement.GetProperty("tags")[1].GetString());
		}

		[Fact]
		public void Process_PendingValuesPauseBeforeGrowing_PreservesFinalValues()
		{
			var chunker = new JsonStreamChunker();
			var lines = new[]
			{
				"""{"summary":"","category":"","keyPoints":[],"sentiment":""}""",
				"""{"summary":"Hello","category":"","keyPoints":[],"sentiment":""}""",
				"""{"summary":"Hello there","category":"","keyPoints":[],"sentiment":""}""",
				"""{"summary":"Hello there friend.","category":"greeting","keyPoints":["hi","yo"],"sentiment":"positive"}"""
			};

			var chunks = new List<string>();
			foreach (var line in lines)
				chunks.Add(chunker.Process(line));
			chunks.Add(chunker.Flush());
			var concatenated = string.Concat(chunks);

			Assert.True(IsValidJson(concatenated),
				$"Invalid JSON: {concatenated}\n\nChunks:\n[{string.Join("], [", chunks)}]");
			var doc = JsonDocument.Parse(concatenated);
			Assert.Equal("Hello there friend.", doc.RootElement.GetProperty("summary").GetString());
			Assert.Equal("greeting", doc.RootElement.GetProperty("category").GetString());
			Assert.Equal("hi", doc.RootElement.GetProperty("keyPoints")[0].GetString());
			Assert.Equal("yo", doc.RootElement.GetProperty("keyPoints")[1].GetString());
			Assert.Equal("positive", doc.RootElement.GetProperty("sentiment").GetString());
		}

		[Fact]
		public void Process_EmptyObjectInArray_ProducesValidJson()
		{
			// Test pattern from mount-fuji line 35: {"activities": [{}, ...]}
			var chunker = new JsonStreamChunker();

			var chunks = new List<string>();
			chunks.Add(chunker.Process("""{"activities": [{}]}"""));
			chunks.Add(chunker.Process("""{"activities": [{}, {"description": "Hello"}]}"""));
			chunks.Add(chunker.Flush());

			var concatenated = string.Concat(chunks);

			Assert.True(IsValidJson(concatenated), $"Invalid JSON: {concatenated}");
			var doc = JsonDocument.Parse(concatenated);
			Assert.Equal(2, doc.RootElement.GetProperty("activities").GetArrayLength());
		}

		[Fact]
		public void Process_RootLevelPropertyAppearsMidStream_ProducesValidJson()
		{
			// Test pattern: deep nesting first, then new root property appears
			var chunker = new JsonStreamChunker();

			var chunks = new List<string>();
			chunks.Add(chunker.Process("""{"days": [{"activities": [{"title": "Hello"}]}]}"""));
			chunks.Add(chunker.Process("""{"days": [{"activities": [{"title": "Hello World"}]}], "title": "Trip"}"""));
			chunks.Add(chunker.Flush());

			var concatenated = string.Concat(chunks);

			Assert.True(IsValidJson(concatenated), $"Invalid JSON: {concatenated}");
			var doc = JsonDocument.Parse(concatenated);
			Assert.Equal("Trip", doc.RootElement.GetProperty("title").GetString());
			Assert.Equal("Hello World", doc.RootElement.GetProperty("days")[0].GetProperty("activities")[0].GetProperty("title").GetString());
		}

		[Fact]
		public void Process_DeeplyNestedWithMultipleArrays_ProducesValidJson()
		{
			// Complex nesting: days[] with activities[] inside
			var chunker = new JsonStreamChunker();

			var chunks = new List<string>();
			chunks.Add(chunker.Process("""{"days": [{"activities": [{"title": "Drive"}]}]}"""));
			chunks.Add(chunker.Process("""{"days": [{"activities": [{"title": "Drive"}, {"title": "Lunch"}]}]}"""));
			chunks.Add(chunker.Process("""{"days": [{"activities": [{"title": "Drive"}, {"title": "Lunch"}]}, {"activities": []}]}"""));
			chunks.Add(chunker.Flush());

			var concatenated = string.Concat(chunks);

			Assert.True(IsValidJson(concatenated), $"Invalid JSON: {concatenated}");
			var doc = JsonDocument.Parse(concatenated);
			Assert.Equal(2, doc.RootElement.GetProperty("days").GetArrayLength());
			Assert.Equal(2, doc.RootElement.GetProperty("days")[0].GetProperty("activities").GetArrayLength());
		}
	}
}
