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
		public void Process_NestedPendingStringsPauseThenResume_PreservesFinalValues()
		{
			var chunker = new JsonStreamChunker();
			var lines = new[]
			{
				"""{"outer":{"a":"start","b":"other"}}""",
				"""{"outer":{"a":"start","b":"other"}}""",
				"""{"outer":{"a":"started","b":"other"}}"""
			};

			var chunks = lines.Select(chunker.Process).ToList();
			chunks.Add(chunker.Flush());
			var concatenated = string.Concat(chunks);

			Assert.Equal("", chunks[1]);
			Assert.True(IsValidJson(concatenated),
				$"Invalid JSON: {concatenated}\n\nChunks:\n[{string.Join("], [", chunks)}]");
			using var doc = JsonDocument.Parse(concatenated);
			Assert.Equal("started", doc.RootElement.GetProperty("outer").GetProperty("a").GetString());
			Assert.Equal("other", doc.RootElement.GetProperty("outer").GetProperty("b").GetString());
		}

		[Fact]
		public void Process_NestedPendingContainersPauseThenResume_PreservesFinalContents()
		{
			var chunker = new JsonStreamChunker();
			var lines = new[]
			{
				"""{"outer":{"a":["start"],"b":{"name":"other"}}}""",
				"""{"outer":{"a":["start"],"b":{"name":"other"}}}""",
				"""{"outer":{"a":["started","next"],"b":{"name":"other"}}}"""
			};

			var chunks = lines.Select(chunker.Process).ToList();
			chunks.Add(chunker.Flush());
			var concatenated = string.Concat(chunks);

			Assert.Equal("", chunks[1]);
			Assert.True(IsValidJson(concatenated),
				$"Invalid JSON: {concatenated}\n\nChunks:\n[{string.Join("], [", chunks)}]");
			using var doc = JsonDocument.Parse(concatenated);
			Assert.Equal("started", doc.RootElement.GetProperty("outer").GetProperty("a")[0].GetString());
			Assert.Equal("next", doc.RootElement.GetProperty("outer").GetProperty("a")[1].GetString());
			Assert.Equal("other", doc.RootElement.GetProperty("outer").GetProperty("b").GetProperty("name").GetString());
		}

		[Fact]
		public void Process_PendingNestedStrings_ParentCloses_EmitsWithinParent()
		{
			var chunker = new JsonStreamChunker();
			var lines = new[]
			{
				"""{"outer":{"a":"","b":""}}""",
				"""{"outer":{"a":"first","b":"second"},"after":true}"""
			};

			var chunks = lines.Select(chunker.Process).ToList();
			chunks.Add(chunker.Flush());
			var concatenated = string.Concat(chunks);

			Assert.True(IsValidJson(concatenated),
				$"Invalid JSON: {concatenated}\n\nChunks:\n[{string.Join("], [", chunks)}]");
			using var doc = JsonDocument.Parse(concatenated);
			Assert.Equal("first", doc.RootElement.GetProperty("outer").GetProperty("a").GetString());
			Assert.Equal("second", doc.RootElement.GetProperty("outer").GetProperty("b").GetString());
			Assert.True(doc.RootElement.GetProperty("after").GetBoolean());
		}

		[Fact]
		public void Process_PendingNestedContainers_ParentCloses_EmitsWithinParent()
		{
			var chunker = new JsonStreamChunker();
			var lines = new[]
			{
				"""{"outer":{"a":[],"b":{}}}""",
				"""{"outer":{"a":[1],"b":{"value":2}},"after":true}"""
			};

			var chunks = lines.Select(chunker.Process).ToList();
			chunks.Add(chunker.Flush());
			var concatenated = string.Concat(chunks);

			Assert.True(IsValidJson(concatenated),
				$"Invalid JSON: {concatenated}\n\nChunks:\n[{string.Join("], [", chunks)}]");
			using var doc = JsonDocument.Parse(concatenated);
			Assert.Equal(1, doc.RootElement.GetProperty("outer").GetProperty("a")[0].GetInt32());
			Assert.Equal(2, doc.RootElement.GetProperty("outer").GetProperty("b").GetProperty("value").GetInt32());
			Assert.True(doc.RootElement.GetProperty("after").GetBoolean());
		}

		[Fact]
		public void Process_PendingNestedStrings_NextArrayItem_EmitsWithinPreviousItem()
		{
			var chunker = new JsonStreamChunker();
			var lines = new[]
			{
				"""{"items":[{"inner":{"a":"","b":""}}]}""",
				"""{"items":[{"inner":{"a":"first","b":"second"}},{"inner":{"a":"next","b":"later"}}]}"""
			};

			var chunks = lines.Select(chunker.Process).ToList();
			chunks.Add(chunker.Flush());
			var concatenated = string.Concat(chunks);

			Assert.True(IsValidJson(concatenated),
				$"Invalid JSON: {concatenated}\n\nChunks:\n[{string.Join("], [", chunks)}]");
			using var doc = JsonDocument.Parse(concatenated);
			var items = doc.RootElement.GetProperty("items");
			Assert.Equal("first", items[0].GetProperty("inner").GetProperty("a").GetString());
			Assert.Equal("second", items[0].GetProperty("inner").GetProperty("b").GetString());
			Assert.Equal("next", items[1].GetProperty("inner").GetProperty("a").GetString());
			Assert.Equal("later", items[1].GetProperty("inner").GetProperty("b").GetString());
		}

		[Fact]
		public void Process_PendingNestedStringAndRootSiblings_EmitsEachOnce()
		{
			var chunker = new JsonStreamChunker();
			var lines = new[]
			{
				"""{"outer":{"a":"","b":""}}""",
				"""{"outer":{"a":"","b":""},"alpha":"","beta":""}""",
				"""{"outer":{"a":"value","b":""},"alpha":"","beta":"","final":1}"""
			};

			var chunks = lines.Select(chunker.Process).ToList();
			chunks.Add(chunker.Flush());
			var concatenated = string.Concat(chunks);

			Assert.True(IsValidJson(concatenated),
				$"Invalid JSON: {concatenated}\n\nChunks:\n[{string.Join("], [", chunks)}]");
			using var doc = JsonDocument.Parse(concatenated);
			Assert.Equal("value", doc.RootElement.GetProperty("outer").GetProperty("a").GetString());
			Assert.Equal("", doc.RootElement.GetProperty("outer").GetProperty("b").GetString());
			Assert.Equal("", doc.RootElement.GetProperty("alpha").GetString());
			Assert.Equal("", doc.RootElement.GetProperty("beta").GetString());
			Assert.Equal(1, doc.RootElement.GetProperty("final").GetInt32());
			Assert.Equal(4, doc.RootElement.EnumerateObject().Count());
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
