using Microsoft.Extensions.AI;

namespace Microsoft.Maui.Essentials.AI.UnitTests;

public partial class StreamingResponseHandlerTests
{
	[Fact]
	public async Task Reasoning_CumulativeEntriesAndSegments_DeduplicateByNativeIdentity()
	{
		var handler = new StreamingResponseHandler(new PlainTextStreamChunker(), "local");
		handler.ProcessReasoning("first", "segment-a", "Think ", null);
		handler.ProcessReasoning("first", "segment-a", "Think ", null);
		handler.ProcessReasoning("first", "segment-a", "Think carefully", null);
		handler.ProcessReasoning("first", "segment-b", "\nNext", null);
		handler.ProcessReasoning("first", "segment-b", "\nNext", null);
		handler.ProcessToolCall("opaque-call", "lookup", "{}");
		handler.ProcessToolResult("opaque-call", "code");
		handler.ProcessReasoning("second", "segment-a", "Think ", null);
		handler.ProcessReasoning("first", "segment-a", "Think carefully", null);
		handler.ProcessContent("Answer", "answer");
		handler.Complete(new() { InputTokenCount = 2, OutputTokenCount = 5, TotalTokenCount = 7 });
		var updates = await ReadAll(handler);

		Assert.Equal(new[] { "Think ", "carefully", "\nNext", "Think " },
			updates.SelectMany(update => update.Contents).OfType<TextReasoningContent>().Select(content => content.Text));
		Assert.Equal(new[] { "first", "first", "first", null, null, "second", "answer", null },
			updates.Select(update => update.MessageId));
		Assert.All(updates, update => Assert.Equal("local", update.ModelId));
		Assert.Equal(ChatRole.Tool, updates[4].Role);
		Assert.Equal("opaque-call", Assert.IsType<FunctionCallContent>(updates[3].Contents[0]).CallId);
		Assert.Equal("opaque-call", Assert.IsType<FunctionResultContent>(updates[4].Contents[0]).CallId);
		Assert.Equal("Answer", Assert.IsType<TextContent>(updates[6].Contents[0]).Text);
		Assert.IsType<UsageContent>(updates[^1].Contents[0]);
		Assert.Single(updates.SelectMany(update => update.Contents).OfType<UsageContent>());
	}

	[Fact]
	public async Task Reasoning_FinalProtectionOnlyChanges_AreRetainedWithoutDuplicateText()
	{
		var handler = new StreamingResponseHandler(new PlainTextStreamChunker());
		handler.ProcessReasoning("entry", "segment", "Original", null);
		handler.ProcessReasoning("entry", "segment", "Original", "opaque-native-1");
		handler.ProcessReasoning("entry", null, null, "opaque-native-1");
		handler.ProcessReasoning("entry", null, null, "opaque-native-2");
		handler.ProcessReasoning("signature-only", null, null, "opaque-native-3");
		handler.Complete();
		var updates = await ReadAll(handler);
		var reasoning = updates.SelectMany(update => update.Contents).OfType<TextReasoningContent>().ToArray();

		Assert.Equal(4, reasoning.Length);
		Assert.Equal("Original", reasoning[0].Text);
		Assert.All(reasoning.Skip(1), content => Assert.True(string.IsNullOrEmpty(content.Text)));
		Assert.Equal(new[] { null, "opaque-native-1", "opaque-native-2", "opaque-native-3" },
			reasoning.Select(content => content.ProtectedData));
		Assert.Equal("signature-only", updates[^1].MessageId);
	}

	[Fact]
	public async Task Reasoning_OutputNone_SuppressesReasoningButNotAnswerOrUsage()
	{
		var handler = new StreamingResponseHandler(new PlainTextStreamChunker(), includeReasoning: false);
		handler.ProcessReasoning("entry", "segment", "Hidden", "opaque");
		handler.ProcessContent("Visible");
		handler.Complete(new() { OutputTokenCount = 9 });
		var updates = await ReadAll(handler);
		Assert.Equal(2, updates.Count);
		Assert.IsType<TextContent>(updates[0].Contents[0]);
		Assert.IsType<UsageContent>(updates[1].Contents[0]);
	}

	[Theory]
	[InlineData(null, "segment", "Text")]
	[InlineData("entry", null, "Text")]
	public void Reasoning_MissingNativeIdentity_IsRejected(string? entry, string? segment, string text)
	{
		var handler = new StreamingResponseHandler(new PlainTextStreamChunker());
		Assert.ThrowsAny<ArgumentException>(() => handler.ProcessReasoning(entry, segment, text, null));
	}

	[Fact]
	public async Task Reasoning_RewrittenNativeSegment_FailsInsteadOfReturningSuccess()
	{
		var handler = new StreamingResponseHandler(new PlainTextStreamChunker());
		handler.ProcessReasoning("entry", "segment", "Original", null);
		var error = Assert.Throws<InvalidDataException>(() =>
			handler.ProcessReasoning("entry", "segment", "Replaced", null));
		handler.CompleteWithError(error);
		await Assert.ThrowsAsync<InvalidDataException>(() => ReadAll(handler));
	}

	[Fact]
	public async Task Content_NewNativeMessageIdentity_DoesNotShareCumulativeAnswerState()
	{
		var handler = new StreamingResponseHandler(new PlainTextStreamChunker());
		handler.ProcessContent("First", "one");
		handler.ProcessContent("First", "one");
		handler.ProcessReasoning("reasoning", "segment", "Thought", null);
		handler.ProcessContent("Second", "two");
		handler.Complete();
		var updates = await ReadAll(handler);
		Assert.Equal(new[] { "First", "Second" },
			updates.SelectMany(update => update.Contents).OfType<TextContent>().Select(content => content.Text));
		Assert.Equal(new[] { "one", "reasoning", "two" }, updates.Select(update => update.MessageId));
	}
}
