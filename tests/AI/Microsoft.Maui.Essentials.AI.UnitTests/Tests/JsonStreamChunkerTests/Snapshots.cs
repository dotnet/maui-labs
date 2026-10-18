using System.Text.Json;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.UnitTests;

public partial class JsonStreamChunkerTests
{
	public class SnapshotTests
	{
		public static TheoryData<string[]> CapturedWeatherSnapshots => new()
		{
			new[]
			{
				"""{"temperature":6}""",
				"""{"temperature":68}""",
				"""{"temperature":68}""",
				"""{"condition":"sunny","temperature":68}""",
				"""{"condition":"sunny","temperature":68}""",
				"""{"temperature":68,"condition":"sunny","location":""}""",
				"""{"location":"Boston","temperature":68,"condition":"sunny"}""",
				"""{"location":"Boston","temperature":68,"condition":"sunny"}"""
			},
			new[]
			{
				"""{"condition":"sunny"}""",
				"""{"condition":"sunny"}""",
				"""{"condition":"sunny"}""",
				"""{"temperature":72,"condition":"sunny"}""",
				"""{"condition":"sunny","location":"","temperature":72}""",
				"""{"location":"","temperature":72,"condition":"sunny"}""",
				"""{"location":"Boston","temperature":72,"condition":"sunny"}""",
				"""{"temperature":72,"condition":"sunny","location":"Boston"}"""
			}
		};

		[Theory]
		[MemberData(nameof(CapturedWeatherSnapshots))]
		public void Process_CapturedWeatherSnapshots_PreservesFinalValues(string[] snapshots)
		{
			var chunker = new JsonStreamChunker();
			var chunks = snapshots.Select(chunker.Process).ToList();
			chunks.Add(chunker.Flush());

			AssertFinalSnapshot(snapshots[^1], string.Concat(chunks));
			using var actual = JsonDocument.Parse(string.Concat(chunks));
			Assert.Equal("Boston", actual.RootElement.GetProperty("location").GetString());
			Assert.Equal("sunny", actual.RootElement.GetProperty("condition").GetString());
			using var expected = JsonDocument.Parse(snapshots[^1]);
			Assert.Equal(expected.RootElement.GetProperty("temperature").GetInt32(),
				actual.RootElement.GetProperty("temperature").GetInt32());
		}

		[Theory]
		[InlineData("object")]
		[InlineData("array")]
		[InlineData("nestedArrays")]
		public void Process_CapturedWeatherInsideContainers_PreservesFinalValues(string shape)
		{
			foreach (var snapshots in CapturedWeatherSnapshots)
			{
				var chunker = new JsonStreamChunker();
				var wrapped = snapshots.Select(snapshot => Wrap(shape, snapshot)).ToArray();
				var chunks = wrapped.Select(chunker.Process).ToList();
				chunks.Add(chunker.Flush());

				AssertFinalSnapshot(wrapped[^1], string.Concat(chunks));
			}
		}

		[Theory]
		[InlineData("root")]
		[InlineData("object")]
		[InlineData("array")]
		[InlineData("nestedArrays")]
		[InlineData("stringArray")]
		public void Process_DuplicateSnapshotsBeforeStringResumes_StreamsPartialString(string shape)
		{
			const string start = "This long response must stream before the model finishes";
			const string continuation = ", even after duplicate snapshots";
			var chunker = new JsonStreamChunker();
			string Snapshot(string text) => shape == "stringArray"
				? $$"""{"items":[{{JsonSerializer.Serialize(text)}}]}"""
				: Wrap(shape, $$"""{"text":{{JsonSerializer.Serialize(text)}}}""");

			var first = chunker.Process(Snapshot(start));
			Assert.EndsWith(start, first);
			Assert.False(IsValidJson(first));
			Assert.Equal("", chunker.Process(Snapshot(start)));
			Assert.Equal("", chunker.Process(Snapshot(start)));

			var resumed = chunker.Process(Snapshot(start + continuation));
			Assert.Equal(continuation, resumed);
			Assert.Equal("", chunker.Process(Snapshot(start + continuation)));
			var final = first + resumed + chunker.Flush();

			AssertFinalSnapshot(Snapshot(start + continuation), final);
		}

		[Theory]
		[InlineData("6", "68")]
		[InlineData("0", "68")]
		[InlineData("1", "-12.5")]
		[InlineData("1", "1e+20")]
		public void Process_ProvisionalNumberWithoutStructuralProof_UsesLatestValue(string initial, string final)
		{
			var chunker = new JsonStreamChunker();
			var first = chunker.Process($$"""{"value":{{initial}}}""");
			Assert.Equal("{", first);
			Assert.Equal("", chunker.Process($$"""{"value":{{initial}}}"""));
			Assert.Equal("", chunker.Process($$"""{"value":{{final}}}"""));

			AssertFinalSnapshot($$"""{"value":{{final}}}""", first + chunker.Flush());
		}

		[Fact]
		public void Process_ProvisionalLastArrayNumber_UsesLatestValueBeforeNextItem()
		{
			var chunker = new JsonStreamChunker();
			var first = chunker.Process("""{"values":[6]}""");
			var revised = chunker.Process("""{"values":[68]}""");
			var next = chunker.Process("""{"values":[68,9]}""");
			var final = chunker.Process("""{"values":[68,90]}""");

			AssertFinalSnapshot("""{"values":[68,90]}""", first + revised + next + final + chunker.Flush());
		}

		[Fact]
		public void Process_LastArrayNumberIsReplaced_EmitsOnlyLatestValueOnFlush()
		{
			var chunker = new JsonStreamChunker();

			var first = chunker.Process("""{"values":[10]}""");
			var second = chunker.Process("""{"values":[20]}""");
			var final = chunker.Flush();

			Assert.Equal("""{"values":[""", first);
			Assert.Equal("", second);
			Assert.Equal("20]}", final);
			AssertFinalSnapshot("""{"values":[20]}""", first + second + final);
		}

		[Theory]
		[InlineData("""{"text":"hello"}""", """{"text":"world"}""")]
		[InlineData("""{"text":"hello"}""", """{"text":"hell"}""")]
		[InlineData("""{"text":"hello"}""", """{"text":null}""")]
		[InlineData("""{"values":[]}""", """{"values":false}""")]
		[InlineData("""{"text":"hello"}""", """{"text":"hello again","done":true}""", """{"text":"hello","done":true}""")]
		[InlineData("""{"values":[6]}""", """{"values":[68,2]}""", """{"values":[6,2]}""")]
		[InlineData("""{"values":[true]}""", """{"values":[false,2]}""", """{"values":[true,2]}""")]
		[InlineData("""{"values":[null]}""", """{"values":["new",2]}""", """{"values":[null,2]}""")]
		[InlineData("""{"text":"hello"}""", """{}""")]
		[InlineData("""{"values":[1]}""", """{"values":[1,2],"done":"yes"}""", """{"values":[1],"done":"yes"}""")]
		public void Process_RevisionOfEmittedContent_ThrowsInsteadOfSilentlyDroppingChanges(
			string first, string revised, string? completed = null)
		{
			var chunker = new JsonStreamChunker();
			chunker.Process(first);
			if (completed != null)
				chunker.Process(completed);

			Assert.Throws<NotSupportedException>(() => chunker.Process(revised));
		}

		[Fact]
		public void Reset_DuringProvisionalNumber_DiscardsDeferredValue()
		{
			var chunker = new JsonStreamChunker();
			chunker.Process("""{"value":6}""");
			chunker.Reset();

			AssertFinalSnapshot("""{"value":68}""", chunker.Process("""{"value":68}""") + chunker.Flush());
		}

		[Theory]
		[InlineData("root")]
		[InlineData("object")]
		[InlineData("array")]
		public void Process_ProvisionalNumberBesidePartialString_DoesNotBlockStringStreaming(string shape)
		{
			const string start = "A long response that must be visible before completion";
			const string continuation = " while the number remains provisional";
			var chunker = new JsonStreamChunker();
			string Snapshot(int temperature, string text) =>
				Wrap(shape, $$"""{"temperature":{{temperature}},"text":{{JsonSerializer.Serialize(text)}}}""");

			var first = chunker.Process(Snapshot(6, start));
			Assert.EndsWith(start, first);
			Assert.DoesNotContain("temperature", first);
			Assert.Equal("", chunker.Process(Snapshot(6, start)));
			var resumed = chunker.Process(Snapshot(68, start + continuation));
			Assert.Equal(continuation, resumed);
			Assert.Equal("", chunker.Process(Snapshot(68, start + continuation)));

			AssertFinalSnapshot(Snapshot(68, start + continuation), first + resumed + chunker.Flush());
		}

		[Theory]
		[InlineData("6", "-2")]
		[InlineData("true", "false")]
		[InlineData("null", "true")]
		[InlineData("false", "null")]
		[InlineData("null", """{"text":"completed"}""")]
		[InlineData("0", """["completed"]""")]
		[InlineData("false", "\"completed\"")]
		public void Process_UncommittedValueChangesValueOrKind_PreservesFinalSnapshot(string initial, string final)
		{
			foreach (var shape in new[] { "root", "object", "array", "nestedArrays" })
			{
				var chunker = new JsonStreamChunker();
				var first = Wrap(shape, $$"""{"value":{{initial}}}""");
				var last = Wrap(shape, $$"""{"value":{{final}}}""");
				var chunks = new List<string>
				{
					chunker.Process(first),
					chunker.Process(first),
					chunker.Process(last),
					chunker.Process(last),
					chunker.Flush()
				};

				AssertFinalSnapshot(last, string.Concat(chunks));
			}
		}

		[Theory]
		[InlineData("true", "false")]
		[InlineData("null", "72")]
		[InlineData("0", """{"text":"completed"}""")]
		[InlineData("false", """["completed"]""")]
		[InlineData("null", "\"completed\"")]
		public void Process_ProvisionalLastArrayItemChangesKind_PreservesFinalSnapshot(string initial, string final)
		{
			var chunker = new JsonStreamChunker();
			var first = $$"""{"values":[{{initial}}]}""";
			var last = $$"""{"values":[{{final}}]}""";
			var chunks = new[]
			{
				chunker.Process(first),
				chunker.Process(first),
				chunker.Process(last),
				chunker.Process(last),
				chunker.Flush()
			};

			AssertFinalSnapshot(last, string.Concat(chunks));
		}

		[Theory]
		[InlineData("""{"value":"","other":""}""", """{"value":72,"other":"done"}""")]
		[InlineData("""{"value":[],"other":""}""", """{"value":false,"other":"done"}""")]
		[InlineData("""{"value":{},"other":""}""", """{"value":null,"other":"done"}""")]
		[InlineData("""{"value":["old"],"other":""}""", """{"value":{"text":"done"},"other":""}""")]
		public void Process_DeferredGrowableChangesKind_PreservesFinalSnapshot(string first, string final)
		{
			var chunker = new JsonStreamChunker();
			var result = chunker.Process(first) + chunker.Process(final) + chunker.Flush();

			AssertFinalSnapshot(final, result);
		}

		[Fact]
		public void Process_AllScalarKindsChangeBesideStreamingString_DoesNotCloseTheString()
		{
			var chunker = new JsonStreamChunker();
			var first = chunker.Process("""{"number":6,"flag":false,"data":null,"text":"A long response"}""");
			Assert.Equal("""{"text":"A long response""", first);
			var second = chunker.Process("""{"number":68,"flag":true,"data":{"value":null},"text":"A long response keeps streaming"}""");
			Assert.Equal(" keeps streaming", second);
			Assert.Equal("", chunker.Process("""{"text":"A long response keeps streaming","data":{"value":true},"flag":true,"number":68}"""));
			var third = chunker.Process("""{"number":68,"flag":true,"data":{"value":false},"text":"A long response keeps streaming until completion"}""");
			Assert.Equal(" until completion", third);

			AssertFinalSnapshot(
				"""{"number":68,"flag":true,"data":{"value":false},"text":"A long response keeps streaming until completion"}""",
				first + second + third + chunker.Flush());
		}

		[Fact]
		public void Process_NewArrayStringPausesThenResumes_StreamsItsExtension()
		{
			var chunker = new JsonStreamChunker();
			var first = chunker.Process("""{"values":["first"]}""");
			var second = chunker.Process("""{"values":["first","partial"]}""");
			Assert.EndsWith("partial", second);
			Assert.Equal("", chunker.Process("""{"values":["first","partial"]}"""));
			var resumed = chunker.Process("""{"values":["first","partial and completed"]}""");
			Assert.Equal(" and completed", resumed);

			AssertFinalSnapshot("""{"values":["first","partial and completed"]}""",
				first + second + resumed + chunker.Flush());
		}

		[Fact]
		public void Flush_NestedAndRootPendingValues_EmitsEachPropertyOnce()
		{
			var chunker = new JsonStreamChunker();
			var first = chunker.Process("""{"outer":{"a":"","b":"","text":""},"z":"last"}""");
			var second = chunker.Process("""{"outer":{"a":"first","b":"second","text":"long"},"z":"last"}""");
			var third = chunker.Process("""{"outer":{"a":"first","b":"second","text":"long response"},"z":"last"}""");
			Assert.EndsWith("long response", third);

			AssertFinalSnapshot(
				"""{"outer":{"a":"first","b":"second","text":"long response"},"z":"last"}""",
				first + second + third + chunker.Flush());
			Assert.Equal("", chunker.Flush());
		}

		private static string Wrap(string shape, string json) => shape switch
		{
			"root" => json,
			"object" => $$"""{"outer":{{json}}}""",
			"array" => $$"""{"items":[{{json}}]}""",
			"nestedArrays" => $$"""{"groups":[[{{json}}]]}""",
			_ => throw new ArgumentException("Unknown shape", nameof(shape))
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
