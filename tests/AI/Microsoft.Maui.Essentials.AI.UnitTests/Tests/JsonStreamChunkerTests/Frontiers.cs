using System.Text.Json;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.UnitTests;

public partial class JsonStreamChunkerTests
{
	public class FrontierTests
	{
		[Theory]
		[InlineData("root")]
		[InlineData("object")]
		[InlineData("array")]
		public void Process_PlateauedStringAndGrowingPendingString_MovesStreamingFrontier(string shape)
		{
			var snapshots = new[]
			{
				"""{"a":"x","b":""}""",
				"""{"a":"xy","b":""}""",
				"""{"a":"xyz","b":"abc"}""",
				"""{"a":"xyz","b":"abcdef"}""",
				"""{"a":"xyz","b":"abcdefghi"}"""
			}.Select(json => Wrap(shape, json)).ToArray();
			var chunker = new JsonStreamChunker();
			var chunks = snapshots.Select(chunker.Process).ToList();

			Assert.EndsWith("xy", chunks[1]);
			Assert.Equal("z", chunks[2]);
			Assert.EndsWith(""","b":"abcdef""", chunks[3]);
			Assert.Equal("ghi", chunks[4]);
			Assert.Equal("", chunker.Process(snapshots[^1]));
			AssertFinalSnapshot(snapshots[^1], string.Concat(chunks) + chunker.Flush());
		}

		[Theory]
		[InlineData("root")]
		[InlineData("object")]
		[InlineData("array")]
		public void Process_PlateauedTitleAndGrowingPendingDetails_StreamsNestedString(string shape)
		{
			var snapshots = new[]
			{
				"""{"title":"Day","details":{"text":""}}""",
				"""{"title":"Day one","details":{"text":""}}""",
				"""{"title":"Day one","details":{"text":"A long nested"}}""",
				"""{"title":"Day one","details":{"text":"A long nested response"}}""",
				"""{"title":"Day one","details":{"text":"A long nested response continues"}}"""
			}.Select(json => Wrap(shape, json)).ToArray();
			var chunker = new JsonStreamChunker();
			var chunks = snapshots.Select(chunker.Process).ToList();

			Assert.EndsWith("Day one", chunks[1]);
			Assert.EndsWith("A long nested", chunks[2]);
			Assert.Equal(" response", chunks[3]);
			Assert.Equal(" continues", chunks[4]);
			AssertFinalSnapshot(snapshots[^1], string.Concat(chunks) + chunker.Flush());
		}

		[Fact]
		public void Process_PlateauedSummaryAndGrowingPendingArray_StreamsNestedItem()
		{
			var chunker = new JsonStreamChunker();
			var snapshots = new[]
			{
				"""{"summary":"A","items":[]}""",
				"""{"summary":"A summary","items":[]}""",
				"""{"summary":"A summary","items":[{"text":"A long nested"}]}""",
				"""{"summary":"A summary","items":[{"text":"A long nested response"}]}""",
				"""{"summary":"A summary","items":[{"text":"A long nested response continues"}]}"""
			};
			var chunks = snapshots.Select(chunker.Process).ToList();

			Assert.EndsWith("A summary", chunks[1]);
			Assert.EndsWith("A long nested", chunks[2]);
			Assert.Equal(" response", chunks[3]);
			Assert.Equal(" continues", chunks[4]);
			AssertFinalSnapshot(snapshots[^1], string.Concat(chunks) + chunker.Flush());
		}

		[Theory]
		[InlineData("[]", "root")]
		[InlineData("\"\"", "root")]
		[InlineData("[]", "array")]
		[InlineData("\"\"", "array")]
		public void Process_NestedGrowthAndNewAncestorSibling_PreservesGrowthBeforeClosing(string pending, string shape)
		{
			var snapshots = new[]
			{
				$$$"""{"p0":{{{pending}}},"p1":{"a":"x"}}""",
				$$$"""{"p0":{{{pending}}},"p1":{"a":"xy"}}""",
				$$$"""{"p0":{{{pending}}},"p1":{"a":"xy","b":"z"},"p2":"q"}"""
			}.Select(json => Wrap(shape, json)).ToArray();
			var chunker = new JsonStreamChunker();
			var chunks = snapshots.Select(chunker.Process).ToList();

			Assert.Contains("z", chunks[2]);
			AssertFinalSnapshot(snapshots[^1], string.Concat(chunks) + chunker.Flush());
		}

		[Fact]
		public void Process_NestedGrowthAndNewArrayItem_PreservesGrowthBeforeClosing()
		{
			var snapshots = new[]
			{
				"""{"items":[{"p0":[],"p1":{"a":"x"}}]}""",
				"""{"items":[{"p0":[],"p1":{"a":"xy"}}]}""",
				"""{"items":[{"p0":[],"p1":{"a":"xy","b":"z"}},{"p0":[],"p1":{"a":"next"}}]}"""
			};
			var chunker = new JsonStreamChunker();
			var chunks = snapshots.Select(chunker.Process).ToList();

			Assert.Contains("z", chunks[2]);
			AssertFinalSnapshot(snapshots[^1], string.Concat(chunks) + chunker.Flush());
		}

		[Theory]
		[InlineData("a.b")]
		[InlineData("a[b]")]
		[InlineData("[0]")]
		[InlineData("]")]
		[InlineData("~")]
		[InlineData("~1")]
		[InlineData("")]
		public void Process_LiteralPropertyName_StreamsWithoutPathCollisions(string name)
		{
			foreach (var shape in new[] { "root", "object", "array" })
			{
				string Snapshot(string value) => Wrap(shape, JsonSerializer.Serialize(new Dictionary<string, string>
				{
					[name] = value,
					["other"] = "unchanged"
				}));
				var chunker = new JsonStreamChunker();
				var first = chunker.Process(Snapshot("x"));
				var second = chunker.Process(Snapshot("xx"));
				Assert.EndsWith("xx", second);
				var third = chunker.Process(Snapshot("xxx"));
				Assert.Equal("x", third);

				AssertFinalSnapshot(Snapshot("xxx"), first + second + third + chunker.Flush());
			}
		}

		[Theory]
		[InlineData("""{"a":{"b":"x"},"a.b":"y"}""", """{"a.b":"y","a":{"b":"xx"}}""")]
		[InlineData("""{"a":["x"],"a[0]":"y"}""", """{"a[0]":"y","a":["xx"]}""")]
		[InlineData("""{"~1":"x",".":"y"}""", """{".":"y","~1":"xx"}""")]
		[InlineData("""{"a.b":{"[0]":"x"},"a":{"b":["y"]}}""", """{"a":{"b":["y"]},"a.b":{"[0]":"xx"}}""")]
		public void Process_StructuralAndLiteralPathsCoexist_PreservesBoth(string first, string last)
		{
			var chunker = new JsonStreamChunker();
			var output = chunker.Process(first) + chunker.Process(last) + chunker.Flush();

			AssertFinalSnapshot(last, output);
		}

		[Theory]
		[InlineData("root", 17)]
		[InlineData("object", 31)]
		[InlineData("array", 53)]
		public void Process_SeededReorderingAndPlateaus_StreamsEachFrontier(string shape, int seed)
		{
			var random = new Random(seed);
			var values = new Dictionary<string, string>
			{
				["first.name"] = "",
				["second[0]"] = "",
				["third~name"] = ""
			};
			var chunker = new JsonStreamChunker();
			var chunks = new List<string>();
			string Snapshot() => Wrap(shape, JsonSerializer.Serialize(
				values.OrderBy(_ => random.Next()).ToDictionary(pair => pair.Key, pair => pair.Value)));

			chunks.Add(chunker.Process(Snapshot()));
			foreach (var key in values.Keys.ToArray())
			{
				for (var part = 0; part < 6; part++)
				{
					var extension = $" {key}:{part}";
					values[key] += extension;
					var chunk = chunker.Process(Snapshot());
					Assert.EndsWith(extension, chunk);
					chunks.Add(chunk);
					if (random.Next(2) == 0)
					{
						var plateau = chunker.Process(Snapshot());
						Assert.Equal("", plateau);
						chunks.Add(plateau);
					}
				}
			}

			AssertFinalSnapshot(Snapshot(), string.Concat(chunks) + chunker.Flush());
		}

		[Theory]
		[InlineData(7)]
		[InlineData(29)]
		public void Process_SeededNestedFrontiersAndScalarChanges_StreamsBeforeFlush(int seed)
		{
			var random = new Random(seed);
			var details = new Dictionary<string, object?> { ["long.text"] = "" };
			var day = new Dictionary<string, object?>
			{
				["title[0]"] = "",
				["details"] = details,
				["temperature"] = 6
			};
			var root = new Dictionary<string, object?>
			{
				["header"] = "",
				["days"] = new object[] { day },
				["summary"] = "",
				["active"] = false
			};
			var frontiers = new[]
			{
				(Values: root, Key: "header"),
				(Values: day, Key: "title[0]"),
				(Values: details, Key: "long.text"),
				(Values: root, Key: "summary")
			};
			var chunker = new JsonStreamChunker();
			var chunks = new List<string>();
			string Snapshot() => JsonSerializer.Serialize(Shuffle(root, random));

			chunks.Add(chunker.Process(Snapshot()));
			foreach (var (values, key) in frontiers)
			{
				for (var part = 0; part < 6; part++)
				{
					var extension = $" {key}:{part}";
					values[key] = (string)values[key]! + extension;
					if (key == "title[0]" || key == "long.text")
						day["temperature"] = part == 0 ? 6 : 68;
					root["active"] = part % 2 == 0;
					var chunk = chunker.Process(Snapshot());
					if (part >= 2)
						Assert.EndsWith(extension, chunk);
					chunks.Add(chunk);
					if (random.Next(2) == 0)
					{
						var plateau = chunker.Process(Snapshot());
						Assert.Equal("", plateau);
						chunks.Add(plateau);
					}
				}
			}

			AssertFinalSnapshot(Snapshot(), string.Concat(chunks) + chunker.Flush());
		}

		[Theory]
		[InlineData("[]")]
		[InlineData("42")]
		[InlineData("\"text\"")]
		[InlineData("true")]
		[InlineData("null")]
		public void Process_NonObjectRoot_ThrowsExplicitly(string snapshot)
		{
			var chunker = new JsonStreamChunker();

			Assert.Throws<NotSupportedException>(() => chunker.Process(snapshot));
			AssertFinalSnapshot("""{"value":"valid"}""", chunker.Process("""{"value":"valid"}""") + chunker.Flush());
		}

		[Theory]
		[InlineData("")]
		[InlineData(" ")]
		[InlineData("{")]
		[InlineData("""{"value":"unterminated""")]
		[InlineData("""{"value":1,}""")]
		public void Process_InvalidSnapshot_ThrowsWithoutChangingState(string snapshot)
		{
			var chunker = new JsonStreamChunker();
			var first = chunker.Process("""{"value":"start"}""");

			Assert.ThrowsAny<JsonException>(() => chunker.Process(snapshot));
			AssertFinalSnapshot("""{"value":"started"}""",
				first + chunker.Process("""{"value":"started"}""") + chunker.Flush());
		}

		private static string Wrap(string shape, string json) => shape switch
		{
			"root" => json,
			"object" => $$"""{"outer":{{json}}}""",
			"array" => $$"""{"items":[{{json}}]}""",
			_ => throw new ArgumentException("Unknown shape", nameof(shape))
		};

		private static object? Shuffle(object? value, Random random) => value switch
		{
			Dictionary<string, object?> properties => properties.OrderBy(_ => random.Next())
				.ToDictionary(pair => pair.Key, pair => Shuffle(pair.Value, random)),
			object[] items => items.Select(item => Shuffle(item, random)).ToArray(),
			_ => value
		};

		private static void AssertFinalSnapshot(string expected, string actual)
		{
			using var expectedDocument = JsonDocument.Parse(expected);
			using var actualDocument = JsonDocument.Parse(actual);
			Assert.True(JsonElementsAreEqual(expectedDocument.RootElement, actualDocument.RootElement),
				$"Expected: {expected}\nActual: {actual}");
		}
	}
}
