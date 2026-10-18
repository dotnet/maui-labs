using Xunit;

namespace Microsoft.Maui.Essentials.AI.UnitTests;

public partial class JsonStreamingRoundtripTests
{
	/// <summary>
	/// Tests for simple progressions and basic scenarios.
	/// </summary>
	public class SimpleProgressionTests
	{
		[Fact]
		public void Roundtrip_SimpleProgression_DeserializesCorrectly()
		{
			// Arrange
			var lines = new[]
			{
				"""{"name":"Matthew"}""",
				"""{"name":"Matthew Leibowitz"}""",
				"""{"age":32,"name":"Matthew Leibowitz"}"""
			};
			var expectedChunks = new[]
			{
				"{\"name\":\"Matthew",
				" Leibowitz",
				"\"",
				",\"age\":32}"
			};
			var expectedModels = new SimpleModel?[]
			{
				new SimpleModel { Name = "Matthew" },
				new SimpleModel { Name = "Matthew Leibowitz" },
				new SimpleModel { Name = "Matthew Leibowitz" },
				new SimpleModel { Name = "Matthew Leibowitz", Age = 32 }
			};

			// Act
			var (chunks, models) = ProcessLinesWithChunks<SimpleModel>(lines);

			// Assert chunks
			Assert.Equal(expectedChunks.Length, chunks.Count);
			for (int i = 0; i < expectedChunks.Length; i++)
				Assert.Equal(expectedChunks[i], chunks[i]);

			// Assert models at each step
			Assert.Equal(expectedModels.Length, models.Count);
			for (int i = 0; i < expectedModels.Length; i++)
				Assert.Equivalent(expectedModels[i], models[i]);
		}

		[Fact]
		public void Roundtrip_EmptyStringGrows_DeserializesCorrectly()
		{
			// Arrange
			var lines = new[]
			{
				"""{"name": ""}""",
				"""{"name": "Matthew"}""",
				"""{"name": "Matthew", "description": "Developer"}"""
			};
			// Note: Due to 1-chunk delay for strings, the first chunk doesn't have a closed string
			// The deserializer handles incomplete JSON gracefully, returning partial objects
			var expectedModels = new SimpleModel?[]
			{
				new SimpleModel { Name = "" }, // First chunk: {"name":" - incomplete string deserializes as empty
				new SimpleModel { Name = "Matthew" }, // Second chunk closes empty and extends to Matthew
				new SimpleModel { Name = "Matthew", Description = "Developer" }, // Third adds description
				new SimpleModel { Name = "Matthew", Description = "Developer" } // Flush
			};

			// Act
			var models = ProcessLines<SimpleModel>(lines);

			// Assert at each step
			Assert.Equal(expectedModels.Length, models.Count);
			for (int i = 0; i < expectedModels.Length; i++)
			{
				if (expectedModels[i] == null)
					Assert.Null(models[i]);
				else
					Assert.Equivalent(expectedModels[i], models[i]);
			}
		}

		[Fact]
		public void Roundtrip_PropertyReordering_DeserializesCorrectly()
		{
			// This tests the scenario where AI reorders properties between chunks
			// Both properties are strings on the first line, so both go to pending
			var lines = new[]
			{
				"""{"name": "A", "description": "B"}""",
				"""{"description": "B", "name": "A", "active": true}"""  // reordered + new property
			};
			// Note: With 2 strings in first line, both go to pending (cannot determine which is partial)
			// The deserializer may return an empty object from incomplete JSON
			// The new sibling closes the strings; the provisional scalar waits for flush.
			var expectedModels = new SimpleModel?[]
			{
				new SimpleModel { }, // First chunk: two pending strings, returns empty model
				new SimpleModel { Name = "A", Description = "B" }, // Scalar remains deferred
				new SimpleModel { Name = "A", Description = "B", Active = true } // Flush
			};

			// Act
			var models = ProcessLines<SimpleModel>(lines);

			// Assert at each step
			Assert.Equal(expectedModels.Length, models.Count);
			for (int i = 0; i < expectedModels.Length; i++)
				Assert.Equivalent(expectedModels[i], models[i]);
		}

		[Fact]
		public void Roundtrip_SingleProperty_DeserializesCorrectly()
		{
			var lines = new[]
			{
				"""{"name":"Test"}"""
			};
			var expectedModels = new SimpleModel?[]
			{
				new SimpleModel { Name = "Test" },
				new SimpleModel { Name = "Test" }
			};

			var models = ProcessLines<SimpleModel>(lines);

			Assert.Equal(expectedModels.Length, models.Count);
			for (int i = 0; i < expectedModels.Length; i++)
				Assert.Equivalent(expectedModels[i], models[i]);
		}

		[Fact]
		public void Roundtrip_NumberOnly_DeserializesCorrectly()
		{
			var lines = new[]
			{
				"""{"age":25}""",
				"""{"age":25,"name":"John"}"""
			};
			var expectedModels = new SimpleModel?[]
			{
				new SimpleModel { },
				new SimpleModel { Name = "John" },
				new SimpleModel { Age = 25, Name = "John" }
			};

			var models = ProcessLines<SimpleModel>(lines);

			Assert.Equal(expectedModels.Length, models.Count);
			for (int i = 0; i < expectedModels.Length; i++)
				Assert.Equivalent(expectedModels[i], models[i]);
		}

		[Fact]
		public void Roundtrip_BooleanProperty_DeserializesCorrectly()
		{
			var lines = new[]
			{
				"""{"active":true}""",
				"""{"active":true,"name":"Active User"}"""
			};
			var expectedModels = new SimpleModel?[]
			{
				new SimpleModel { },
				new SimpleModel { Name = "Active User" },
				new SimpleModel { Active = true, Name = "Active User" }
			};

			var models = ProcessLines<SimpleModel>(lines);

			Assert.Equal(expectedModels.Length, models.Count);
			for (int i = 0; i < expectedModels.Length; i++)
				Assert.Equivalent(expectedModels[i], models[i]);
		}

		[Theory]
		[InlineData("""{"age":10,"name":"A long"}""", """{"age":20,"name":"A long response"}""", 20, null)]
		[InlineData("""{"active":true,"name":"A long"}""", """{"active":false,"name":"A long response"}""", null, false)]
		public void Roundtrip_ProvisionalScalarChanges_StreamsStringAndFinalizesLatestScalar(
			string first, string final, int? expectedAge, bool? expectedActive)
		{
			var (chunks, models) = ProcessLinesWithChunks<SimpleModel>(new[] { first, first, final, final });

			Assert.Equal("""{"name":"A long""", chunks[0]);
			Assert.Equal("", chunks[1]);
			Assert.Equal(" response", chunks[2]);
			Assert.Equal("", chunks[3]);
			Assert.All(models.Take(4), model =>
			{
				Assert.NotNull(model);
				Assert.Null(model.Age);
				Assert.Null(model.Active);
			});
			Assert.Equal("A long", models[0]!.Name);
			Assert.Equal("A long response", models[2]!.Name);
			Assert.Equivalent(new SimpleModel
			{
				Name = "A long response",
				Age = expectedAge,
				Active = expectedActive
			}, models[^1]);
		}
	}
}
