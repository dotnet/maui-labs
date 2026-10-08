#if ENABLE_CORE_AI && (IOS || MACCATALYST)
using System.Collections.Concurrent;
using Foundation;
using Microsoft.Extensions.AI;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

[Trait("CoreAI", "true")]
[Trait("Reasoning", "true")]
[Trait(TestTraits.RequiresModel, TestTraits.True)]
public class CoreAIReasoningTests
{
	[Fact]
	public async Task NativeReasoningStream_ReconstructsBothToolSeparatedEntriesFromSameCollectedResponse()
	{
		using var client = CoreAIModelFixture.Create();
		var conversion = Assert.IsType<FoundationModelsChatClient>(client.GetService<FoundationModelsChatClient>());
		var calls = 0;
		var code = $"LOCAL-{Guid.NewGuid():N}";
		var options = CoreAIModelFixture.Options;
		options.Reasoning = new() { Effort = ReasoningEffort.Medium, Output = ReasoningOutput.Full };
		options.Tools = [AIFunctionFactory.Create((string key) =>
		{
			Assert.Equal("alpha", key);
			Interlocked.Increment(ref calls);
			return code;
		}, name: "lookup_code", description: "Read the validation code from this process. Call exactly once when asked for the code.")];
		using var nativeOptions = conversion.ToNative(options, CancellationToken.None);
		using var native = new ChatClientNative(CoreAIModelFixture.DirectoryPath);
		var handler = new StreamingResponseHandler(new PlainTextStreamChunker());
		var callbacks = new ConcurrentQueue<ResponseUpdateNative>();
		var completion = new TaskCompletionSource<ChatResponseNative>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var token = native.StreamResponse([
			new ChatMessageNative
			{
				Role = ChatRoleNative.User,
				Contents = [new TextContentNative("Use lookup_code once with key alpha. Think about why the returned code is authoritative, then return that code.")],
			}], nativeOptions,
			update =>
			{
				try
				{
					callbacks.Enqueue(update);
					switch (update.UpdateType)
					{
						case ResponseUpdateTypeNative.Reasoning:
							handler.ProcessReasoning(update.MessageId, update.SegmentId, update.Text, update.ProtectedData);
							break;
						case ResponseUpdateTypeNative.Content:
							handler.ProcessContent(update.Text, update.MessageId);
							break;
						case ResponseUpdateTypeNative.ToolCall:
							handler.ProcessToolCall(update.ToolCallId, update.ToolCallName, update.ToolCallArguments);
							break;
						case ResponseUpdateTypeNative.ToolResult:
							handler.ProcessToolResult(update.ToolCallId, update.ToolCallResult);
							break;
						default: throw new NotSupportedException($"Unexpected update: {update.UpdateType}");
					}
				}
				catch (Exception error) { completion.TrySetException(error); }
			},
			(response, error) =>
			{
				if (error is not null) completion.TrySetException(new NSErrorException(error));
				else completion.TrySetResult(response!);
			});
		try
		{
			var collected = await completion.Task.WaitAsync(TimeSpan.FromMinutes(4));
			handler.SetModelId(native.ModelIdentifier);
			handler.Complete(new()
			{
				InputTokenCount = collected.InputTokenCount?.Int64Value,
				OutputTokenCount = collected.OutputTokenCount?.Int64Value,
				TotalTokenCount = collected.TotalTokenCount?.Int64Value,
			});
			var updates = new List<ChatResponseUpdate>();
			await foreach (var update in handler.ReadAllAsync(CancellationToken.None)) updates.Add(update);
			var entries = collected.Messages.Where(message => message.Contents.OfType<TextReasoningContentNative>().Any()).ToArray();
			Assert.True(entries.Length >= 2, $"Expected real reasoning before/after the tool, found {entries.Length}.");
			foreach (var entry in entries)
			{
				Assert.False(string.IsNullOrWhiteSpace(entry.MessageId));
				var content = Assert.Single(entry.Contents.OfType<TextReasoningContentNative>());
				Assert.False(string.IsNullOrWhiteSpace(content.Text));
				var deltas = updates.Where(update => update.MessageId == entry.MessageId)
					.SelectMany(update => update.Contents).OfType<TextReasoningContent>().ToArray();
				Assert.Equal(content.Text, string.Concat(deltas.Select(delta => delta.Text)));
				Assert.Equal(content.ProtectedData, deltas.LastOrDefault(delta => delta.ProtectedData is not null)?.ProtectedData);
			}
			var ordered = callbacks.ToArray();
			Assert.True(Array.FindIndex(ordered, update => update.UpdateType == ResponseUpdateTypeNative.Reasoning) <
				Array.FindIndex(ordered, update => update.UpdateType == ResponseUpdateTypeNative.ToolCall));
			var callbackCall = Assert.Single(ordered, update => update.UpdateType == ResponseUpdateTypeNative.ToolCall);
			var callbackResult = Assert.Single(ordered, update => update.UpdateType == ResponseUpdateTypeNative.ToolResult);
			var collectedCall = Assert.Single(collected.Messages.SelectMany(message => message.Contents).OfType<FunctionCallContentNative>());
			var collectedResult = Assert.Single(collected.Messages.SelectMany(message => message.Contents).OfType<FunctionResultContentNative>());
			Assert.Equal(collectedCall.CallId, callbackCall.ToolCallId);
			Assert.Equal(collectedResult.CallId, callbackResult.ToolCallId);
			Assert.Equal(callbackCall.ToolCallId, callbackResult.ToolCallId);
			Assert.Equal(1, calls);
			var answer = string.Concat(updates.SelectMany(update => update.Contents).OfType<TextContent>().Select(text => text.Text));
			Assert.Equal(string.Concat(collected.Messages.SelectMany(message => message.Contents)
				.OfType<TextContentNative>().Select(text => text.Text)), answer);
			Assert.Contains(code, answer, StringComparison.Ordinal);
			Assert.Single(updates.SelectMany(update => update.Contents).OfType<UsageContent>());
		}
		finally { token?.Cancel(); }
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Reasoning_Full_IsSeparateFromAnswerAndUnsignedHistoryCanContinue(bool streaming)
	{
		using var client = CoreAIModelFixture.Create();
		var options = CoreAIModelFixture.Options;
		options.Reasoning = new() { Effort = ReasoningEffort.Medium, Output = ReasoningOutput.Full };
		var history = new List<ChatMessage> { new(ChatRole.User, "My secret word is ORCHID. Think carefully, then briefly acknowledge it.") };
		var response = await Respond(client, history, options, streaming);
		var messages = response.Messages.Where(message => message.Contents.OfType<TextReasoningContent>().Any()).ToArray();
		Assert.NotEmpty(messages);
		Assert.All(messages, message => Assert.False(string.IsNullOrWhiteSpace(message.MessageId)));
		Assert.Contains(messages.SelectMany(message => message.Contents).OfType<TextReasoningContent>(),
			content => !string.IsNullOrWhiteSpace(content.Text));
		Assert.False(string.IsNullOrWhiteSpace(CoreAIModelFixture.Answer(response)));
		Assert.NotNull(response.Usage);
		Assert.True(response.Usage.InputTokenCount > 0);
		Assert.True(response.Usage.OutputTokenCount > 0);
		Assert.Equal(response.Usage.InputTokenCount + response.Usage.OutputTokenCount, response.Usage.TotalTokenCount);
		Assert.Equal(client.GetService<ChatClientMetadata>()?.DefaultModelId, response.ModelId);
		// The pinned Qwen export is unsigned; a future signed fixture must use its actual blob.
		Assert.All(messages.SelectMany(message => message.Contents).OfType<TextReasoningContent>(), content => Assert.Null(content.ProtectedData));
		history.AddRange(response.Messages);
		history.Add(new(ChatRole.User, "Repeat my secret word briefly."));
		options.Reasoning = new() { Effort = ReasoningEffort.None, Output = ReasoningOutput.Full };
		var next = await Respond(client, history, options, streaming);
		Assert.Contains("ORCHID", CoreAIModelFixture.Answer(next), StringComparison.OrdinalIgnoreCase);
		Assert.Empty(next.Messages.SelectMany(message => message.Contents).OfType<TextReasoningContent>());
	}

	[Theory]
	[InlineData(false, false)]
	[InlineData(true, false)]
	[InlineData(false, true)]
	[InlineData(true, true)]
	public async Task Reasoning_NoneEffortOrHiddenOutput_PreservesRealToolRoundTrip(bool streaming, bool hideOutput)
	{
		using var client = CoreAIModelFixture.Create().AsBuilder().UseFunctionInvocation().Build();
		var calls = 0;
		var code = $"LOCAL-{Guid.NewGuid():N}";
		var options = CoreAIModelFixture.Options;
		options.Reasoning = new()
		{
			Effort = hideOutput ? ReasoningEffort.Medium : ReasoningEffort.None,
			Output = hideOutput ? ReasoningOutput.None : ReasoningOutput.Full,
		};
		options.Tools = [AIFunctionFactory.Create((string key) =>
		{
			Assert.False(string.IsNullOrWhiteSpace(key));
			Interlocked.Increment(ref calls);
			return code;
		}, name: "LookupValidationCode", description: "Read the current local validation code for any key. Use the returned code directly.")];
		var response = await Respond(client, [new(ChatRole.User,
			"Call LookupValidationCode with key alpha to read the current validation code, then repeat the returned code unchanged.")], options, streaming);
		Assert.Empty(response.Messages.SelectMany(message => message.Contents).OfType<TextReasoningContent>());
		Assert.Contains(code, CoreAIModelFixture.Answer(response), StringComparison.Ordinal);
		var functionCalls = response.Messages.SelectMany(message => message.Contents).OfType<FunctionCallContent>().ToArray();
		var results = response.Messages.SelectMany(message => message.Contents).OfType<FunctionResultContent>().ToArray();
		Assert.NotEmpty(functionCalls);
		Assert.Equal(functionCalls.Length, functionCalls.Select(call => call.CallId).Distinct().Count());
		Assert.Equal(functionCalls.Length, calls);
		Assert.Equal(functionCalls.Length, results.Length);
		Assert.All(functionCalls, call =>
		{
			Assert.False(string.IsNullOrWhiteSpace(call.CallId));
			Assert.True(call.InformationalOnly);
			Assert.Single(results, result => result.CallId == call.CallId);
		});
		Assert.NotNull(response.Usage);
		Assert.True(response.Usage.InputTokenCount > 0);
		Assert.True(response.Usage.OutputTokenCount > 0);
		Assert.Equal(response.Usage.InputTokenCount + response.Usage.OutputTokenCount, response.Usage.TotalTokenCount);
		Assert.Equal(client.GetService<ChatClientMetadata>()?.DefaultModelId, response.ModelId);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Reasoning_InterruptedStream_AllowsImmediateSchemaRequest(bool cancel)
	{
		using var first = CoreAIModelFixture.Create();
		using var second = CoreAIModelFixture.Create();
		using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(4));
		var options = CoreAIModelFixture.Options;
		options.Reasoning = new() { Effort = ReasoningEffort.Medium, Output = ReasoningOutput.Full };
		var sawReasoning = false;
		async Task Interrupt()
		{
			await foreach (var update in first.GetStreamingResponseAsync(
				[new(ChatRole.User, "Think carefully through how to plan a long journey, then describe the plan in detail.")], options, cancellation.Token))
			{
				if (!update.Contents.OfType<TextReasoningContent>().Any(content => !string.IsNullOrWhiteSpace(content.Text)))
					continue;
				sawReasoning = true;
				if (cancel) cancellation.Cancel();
				else break;
			}
		}
		if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(Interrupt);
		else await Interrupt();
		Assert.True(sawReasoning);
		var nextOptions = CoreAIModelFixture.Options;
		nextOptions.ResponseFormat = ChatResponseFormat.ForJsonSchema<CoreAIChatClientTests.SchemaAnswer>();
		var next = await Respond(second, [new(ChatRole.User, "Return an object with city Paris and count 3.")], nextOptions, streaming: false);
		using var json = System.Text.Json.JsonDocument.Parse(CoreAIModelFixture.Answer(next));
		Assert.Equal("Paris", json.RootElement.GetProperty("city").GetString());
		Assert.Equal(3, json.RootElement.GetProperty("count").GetInt32());
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Reasoning_Summary_FailsExplicitlyBeforeGeneration(bool streaming)
	{
		using var client = CoreAIModelFixture.Create();
		var options = CoreAIModelFixture.Options;
		options.Reasoning = new() { Output = ReasoningOutput.Summary };
		await Assert.ThrowsAsync<NotSupportedException>(() => Respond(client, [new(ChatRole.User, "Hello")], options, streaming));
	}

	private static async Task<ChatResponse> Respond(IChatClient client, IEnumerable<ChatMessage> messages, ChatOptions options, bool streaming)
	{
		using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
		if (!streaming) return await client.GetResponseAsync(messages, options, timeout.Token);
		var updates = new List<ChatResponseUpdate>();
		await foreach (var update in client.GetStreamingResponseAsync(messages, options, timeout.Token)) updates.Add(update);
		return updates.ToChatResponse();
	}
}
#endif
