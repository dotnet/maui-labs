using System.Text.Json;
using Microsoft.Extensions.AI;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.UnitTests;

public partial class StreamingResponseHandlerTests
{
	[Fact]
	public async Task ProcessContent_RepeatedCumulativeSnapshots_PreservesMessageIdentity()
	{
		var handler = new StreamingResponseHandler(new PlainTextStreamChunker(), "apple-intelligence");
		handler.ProcessContent("Hello", "native-response");
		handler.ProcessContent("Hello", "native-response");
		handler.ProcessContent("Hello world", "native-response");
		handler.Complete();

		var updates = await ReadAll(handler);
		Assert.Equal("Hello world", string.Concat(updates.Select(u => u.Text)));
		Assert.All(updates, u =>
		{
			Assert.Equal("native-response", u.MessageId);
			Assert.Equal("apple-intelligence", u.ModelId);
		});
		Assert.Equal("native-response", Assert.Single(updates.ToChatResponse().Messages).MessageId);
	}

	[Fact]
	public async Task ProcessContent_NewIdentity_UsesIndependentCumulativeSnapshot()
	{
		var handler = new StreamingResponseHandler(new PlainTextStreamChunker());
		handler.ProcessContent("Hello", "first");
		handler.ProcessContent("Hello", "second");
		handler.ProcessContent("Hello again", "second");
		handler.Complete();

		var updates = await ReadAll(handler);
		Assert.Equal("Hello", string.Concat(updates.Where(u => u.MessageId == "first").Select(u => u.Text)));
		Assert.Equal("Hello again", string.Concat(updates.Where(u => u.MessageId == "second").Select(u => u.Text)));
		Assert.Equal(2, updates.ToChatResponse().Messages.Count);
	}

	[Fact]
	public async Task ProcessContent_JsonIdentityBoundary_CompletesEachDocumentWithItsOwnId()
	{
		var handler = new StreamingResponseHandler(new JsonStreamChunker());
		handler.ProcessContent("{\"text\":\"first\"}", "first");
		handler.ProcessContent("{\"text\":\"second\"}", "second");
		handler.Complete();

		var updates = await ReadAll(handler);
		foreach (var id in new[] { "first", "second" })
		{
			var json = string.Concat(updates.Where(u => u.MessageId == id).Select(u => u.Text));
			using var document = JsonDocument.Parse(json);
			Assert.Equal(id, document.RootElement.GetProperty("text").GetString());
		}
	}

	[Fact]
	public async Task ProcessToolCall_JsonCompletion_KeepsTextIdWithoutInventingToolMessageIds()
	{
		var handler = new StreamingResponseHandler(new JsonStreamChunker(), "apple-intelligence");
		handler.ProcessContent("{\"text\":\"before\"}", "before");
		handler.ProcessToolCall("call-id", "test_tool", "{}");
		handler.ProcessToolResult("call-id", "result");
		handler.ProcessContent("{\"text\":\"after\"}", "after");
		handler.Complete();

		var updates = await ReadAll(handler);
		var toolUpdates = updates.Where(u => u.Contents.Any(c => c is FunctionCallContent or FunctionResultContent)).ToList();
		Assert.Equal(2, toolUpdates.Count);
		Assert.All(toolUpdates, u => Assert.Null(u.MessageId));
		foreach (var id in new[] { "before", "after" })
		{
			var json = string.Concat(updates.Where(u => u.MessageId == id).Select(u => u.Text));
			using var document = JsonDocument.Parse(json);
			Assert.Equal(id, document.RootElement.GetProperty("text").GetString());
		}
		Assert.All(updates, u => Assert.Equal("apple-intelligence", u.ModelId));
	}

	[Fact]
	public async Task ProcessContent_MissingId_DoesNotInventIdentityOrResetSnapshots()
	{
		var handler = new StreamingResponseHandler(new PlainTextStreamChunker());
		handler.ProcessContent("Hello");
		handler.ProcessContent("Hello world");
		handler.Complete();
		var updates = await ReadAll(handler);
		Assert.Equal("Hello world", string.Concat(updates.Select(u => u.Text)));
		Assert.All(updates, u => Assert.Null(u.MessageId));
	}

	[Fact]
	public async Task ProcessContent_IdAppearingOrTemporarilyMissing_IsNotANewBoundary()
	{
		var handler = new StreamingResponseHandler(new PlainTextStreamChunker());
		handler.ProcessContent("");
		handler.ProcessContent("Hello", "native-id");
		handler.ProcessContent("Hello world");
		handler.ProcessContent("Hello world!", "native-id");
		handler.Complete();
		var updates = await ReadAll(handler);
		Assert.Equal("Hello world!", string.Concat(updates.Select(u => u.Text)));
		Assert.All(updates, u => Assert.Equal("native-id", u.MessageId));
	}

	[Fact]
	public async Task ProcessContent_ReplayedEntriesAcrossTwoTools_PreservesExactTextPerId()
	{
		var handler = new StreamingResponseHandler(new PlainTextStreamChunker(), "apple-intelligence");
		handler.ProcessContent("\n\n", "blank-one");
		handler.ProcessToolCall("call-one", "get_key", "{}");
		handler.ProcessToolResult("call-one", "key");
		handler.ProcessContent("\n\n", "blank-one");
		handler.ProcessContent("\n\n", "blank-two");
		handler.ProcessToolCall("call-two", "get_answer", "{\"key\":\"key\"}");
		handler.ProcessToolResult("call-two", "answer");
		handler.ProcessContent("\n\n", "blank-one");
		handler.ProcessContent("\n\n", "blank-two");
		handler.ProcessContent("Answer", "answer");
		handler.ProcessContent("\n\n", "blank-one");
		handler.ProcessContent("\n\n", "blank-two");
		handler.ProcessContent("Answer", "answer");
		handler.ProcessContent("Answer\r\n\t done \n", "answer");
		handler.Complete();

		var updates = await ReadAll(handler);
		Assert.Equal("\n\n", string.Concat(updates.Where(u => u.MessageId == "blank-one").Select(u => u.Text)));
		Assert.Equal("\n\n", string.Concat(updates.Where(u => u.MessageId == "blank-two").Select(u => u.Text)));
		Assert.Equal("Answer\r\n\t done \n", string.Concat(updates.Where(u => u.MessageId == "answer").Select(u => u.Text)));
		Assert.All(updates, u => Assert.Equal("apple-intelligence", u.ModelId));
		var calls = updates.SelectMany(u => u.Contents.OfType<FunctionCallContent>()).ToList();
		Assert.Equal(new[] { "call-one", "call-two" }, calls.Select(c => c.CallId));
		Assert.All(calls, c => Assert.True(c.InformationalOnly));
		Assert.Equal(2, updates.SelectMany(u => u.Contents.OfType<FunctionResultContent>()).Count());
		Assert.All(updates.Where(u => u.Contents.Any(c => c is FunctionCallContent or FunctionResultContent)),
			u => Assert.Null(u.MessageId));
		var aggregated = updates.ToChatResponse().Messages.Where(m => m.MessageId is not null).ToDictionary(m => m.MessageId!);
		Assert.Equal(3, aggregated.Count);
		Assert.Equal("\n\n", aggregated["blank-one"].Text);
		Assert.Equal("\n\n", aggregated["blank-two"].Text);
		Assert.Equal("Answer\r\n\t done \n", aggregated["answer"].Text);
	}

	[Fact]
	public async Task ProcessContent_OlderEntryGrowsAfterIdentitySwitch_UsesItsOwnCumulativeState()
	{
		var handler = new StreamingResponseHandler(new PlainTextStreamChunker());
		handler.ProcessContent("first \n", "first");
		handler.ProcessContent("second\t", "second");
		handler.ProcessContent("first \ncontinued ", "first");
		handler.ProcessContent("first \ncontinued ");
		handler.ProcessContent("second\tcontinued\r\n", "second");
		handler.Complete();

		var updates = await ReadAll(handler);
		Assert.Equal("first \ncontinued ", string.Concat(updates.Where(u => u.MessageId == "first").Select(u => u.Text)));
		Assert.Equal("second\tcontinued\r\n", string.Concat(updates.Where(u => u.MessageId == "second").Select(u => u.Text)));
		Assert.Equal(new[] { "first \n", "second\t", "continued ", "continued\r\n" }, updates.Select(u => u.Text));
	}

	[Fact]
	public async Task ProcessContent_JsonEntryGrowsAfterOtherEntriesAndTools_PreservesOneDocumentPerId()
	{
		var handler = new StreamingResponseHandler(new JsonStreamChunker(), "apple-intelligence");
		await using var reader = handler.ReadAllAsync(CancellationToken.None).GetAsyncEnumerator();
		handler.ProcessContent("{\"text\":\"first\\n\"}", "first");
		Assert.True(await reader.MoveNextAsync());
		var updates = new List<ChatResponseUpdate> { reader.Current };
		handler.ProcessToolCall("call", "test_tool", "{}");
		Assert.True(await reader.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
		Assert.Single(reader.Current.Contents.OfType<FunctionCallContent>());
		updates.Add(reader.Current);
		handler.ProcessToolResult("call", "result");
		handler.ProcessContent("{\"text\":\"second\\t\"}", "second");
		handler.ProcessContent("{\"text\":\"first\\ncontinued\"}", "first");
		handler.ProcessContent("{\"text\":\"second\\tcontinued\\r\\n\"}", "second");
		handler.Complete();
		while (await reader.MoveNextAsync())
			updates.Add(reader.Current);

		var aggregated = updates.ToChatResponse().Messages
			.Where(m => m.MessageId is not null)
			.GroupBy(m => m.MessageId!)
			.ToDictionary(g => g.Key, g => string.Concat(g.Select(m => m.Text)));
		foreach (var (id, expected) in new[] { ("first", "first\ncontinued"), ("second", "second\tcontinued\r\n") })
		{
			var json = string.Concat(updates.Where(u => u.MessageId == id).Select(u => u.Text));
			using var document = JsonDocument.Parse(json);
			Assert.Equal(expected, document.RootElement.GetProperty("text").GetString());
			Assert.Equal(json, aggregated[id]);
		}
		Assert.All(updates, u => Assert.Equal("apple-intelligence", u.ModelId));
	}

	[Fact]
	public async Task ProcessContent_KnownIdTemporarilyMissingAfterTool_InheritsItsCumulativeState()
	{
		var handler = new StreamingResponseHandler(new PlainTextStreamChunker());
		handler.ProcessContent("Hello", "known");
		handler.ProcessToolCall("call", "test_tool", "{}");
		handler.ProcessToolResult("call", "result");
		handler.ProcessContent("Hello world");
		handler.Complete();

		var updates = await ReadAll(handler);
		var textUpdates = updates.Where(u => u.Contents.OfType<TextContent>().Any()).ToList();
		Assert.Equal("Hello world", string.Concat(textUpdates.Select(u => u.Text)));
		Assert.All(textUpdates, u => Assert.Equal("known", u.MessageId));
	}

	[Fact]
	public async Task ProcessToolCall_KnownId_EmitsImmediatelyWithoutWaitingForMoreContent()
	{
		var handler = new StreamingResponseHandler(new PlainTextStreamChunker());
		await using var updates = handler.ReadAllAsync(CancellationToken.None).GetAsyncEnumerator();
		handler.ProcessContent("before", "known");
		Assert.True(await updates.MoveNextAsync());
		handler.ProcessToolCall("call", "test_tool", "{}");
		Assert.True(await updates.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
		Assert.Equal("call", Assert.Single(updates.Current.Contents.OfType<FunctionCallContent>()).CallId);
		handler.ProcessToolResult("call", "result");
		Assert.True(await updates.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
		Assert.Equal("call", Assert.Single(updates.Current.Contents.OfType<FunctionResultContent>()).CallId);
		handler.Complete();
		Assert.False(await updates.MoveNextAsync());
	}

	[Fact]
	public async Task ProcessContent_ChangedKnownSnapshot_IsNotSilentlySuppressed()
	{
		var handler = new StreamingResponseHandler(new PlainTextStreamChunker());
		handler.ProcessContent("old", "known");
		handler.ProcessContent("other", "other");
		handler.ProcessContent("new", "known");
		handler.Complete();

		var updates = await ReadAll(handler);
		Assert.Equal(new[] { "old", "new" }, updates.Where(u => u.MessageId == "known").Select(u => u.Text));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task CompleteWithError_DoesNotCloseKnownJsonDocuments(bool cancelled)
	{
		var handler = new StreamingResponseHandler(new JsonStreamChunker());
		await using var updates = handler.ReadAllAsync(CancellationToken.None).GetAsyncEnumerator();
		handler.ProcessContent("{\"text\":\"partial\"}", "native-id");
		Assert.True(await updates.MoveNextAsync());
		Assert.Equal("native-id", updates.Current.MessageId);
		Assert.ThrowsAny<JsonException>(() => JsonDocument.Parse(updates.Current.Text));
		Exception error = cancelled ? new OperationCanceledException() : new InvalidOperationException("failed");
		handler.CompleteWithError(error);
		var actual = await Assert.ThrowsAnyAsync<Exception>(async () => await updates.MoveNextAsync());
		Assert.Same(error, actual);
	}

	[Fact]
	public async Task Complete_PerIdJson_EmitsUsageLastWithModelId()
	{
		var usage = new UsageDetails { InputTokenCount = 12, OutputTokenCount = 6 };
		var handler = new StreamingResponseHandler(new JsonStreamChunker(), "apple-intelligence");
		handler.ProcessContent("{\"text\":\"first\"}", "first");
		handler.ProcessToolCall("call", "test_tool", "{}");
		handler.ProcessToolResult("call", "result");
		handler.ProcessContent("{\"text\":\"second\"}", "second");
		handler.Complete(usage);
		var updates = await ReadAll(handler);

		Assert.All(updates, u => Assert.Equal("apple-intelligence", u.ModelId));
		Assert.Same(usage, Assert.Single(updates[^1].Contents.OfType<UsageContent>()).Details);
		Assert.Null(updates[^1].MessageId);
		foreach (var id in new[] { "first", "second" })
		{
			using var document = JsonDocument.Parse(string.Concat(updates.Where(u => u.MessageId == id).Select(u => u.Text)));
			Assert.Equal(id, document.RootElement.GetProperty("text").GetString());
		}
	}

	[Fact]
	public async Task ProcessToolCall_CustomChunker_PreservesLegacyFlushAndReset()
	{
		var chunker = new BufferedChunker();
		var handler = new StreamingResponseHandler(chunker, "apple-intelligence");
		handler.ProcessContent("before", "known");
		handler.ProcessToolCall("call", "test_tool", "{}");
		handler.ProcessToolResult("call", "result");
		handler.ProcessContent("after", "known");
		handler.Complete();
		var updates = await ReadAll(handler);

		Assert.Equal(1, chunker.Resets);
		Assert.Equal(4, updates.Count);
		Assert.Equal("before", updates[0].Text);
		Assert.Single(updates[1].Contents.OfType<FunctionCallContent>());
		Assert.Single(updates[2].Contents.OfType<FunctionResultContent>());
		Assert.Equal("after", updates[3].Text);
		Assert.Equal("known", updates[0].MessageId);
		Assert.Equal("known", updates[3].MessageId);
		Assert.Null(updates[1].MessageId);
		Assert.Null(updates[2].MessageId);
	}

	private sealed class BufferedChunker : StreamChunkerBase
	{
		private string _pending = "";
		public int Resets { get; private set; }

		public override string Process(string completeResponse)
		{
			_pending = completeResponse;
			return "";
		}

		public override string Flush()
		{
			var pending = _pending;
			_pending = "";
			return pending;
		}

		public override void Reset()
		{
			_pending = "";
			Resets++;
		}
	}
}
