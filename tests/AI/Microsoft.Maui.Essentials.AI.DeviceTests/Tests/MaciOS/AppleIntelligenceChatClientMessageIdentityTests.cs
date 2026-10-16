#if IOS || MACCATALYST
using Foundation;
using Microsoft.Extensions.AI;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

public class AppleIntelligenceChatClientMessageIdentityTests(ITestOutputHelper output)
{
	static bool Supports27 => OperatingSystem.IsIOSVersionAtLeast(27) ||
		OperatingSystem.IsMacCatalystVersionAtLeast(27);

	[Fact]
	public void MessageConversion_PreservesNativeEntryIdentity()
	{
		using var native = new ChatMessageNative
		{
			Role = ChatRoleNative.Assistant,
			MessageId = "genuine-entry-id",
			Contents = [new TextContentNative("Hello")]
		};
		var managed = AppleIntelligenceChatClient.FromNative(native);
		Assert.Equal("genuine-entry-id", managed.MessageId);
		using var history = AppleIntelligenceChatClient.ToNative(managed);
		Assert.Equal(native.MessageId, history.MessageId);
	}

	[Fact]
	public void MessageConversion_MissingNativeId_RemainsMissing()
	{
		using var native = new ChatMessageNative { Role = ChatRoleNative.Assistant };
		Assert.Null(AppleIntelligenceChatClient.FromNative(native).MessageId);
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task StreamResponse_TwoChainedTools_MatchesSameRequestCollectedTextByNativeId()
	{
		if (!Supports27)
			return;

		using var client = new AppleIntelligenceChatClient();
		using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
		var key = "lookup-" + Guid.NewGuid().ToString("N")[..8];
		var keyCalls = 0;
		var answerCalls = 0;
		var options = new ChatOptions
		{
			ToolMode = ChatToolMode.Auto,
			Instructions = "Use get_lookup_key first, then lookup_answer with that exact key. " +
				"Complete both tool calls before answering; never guess the key or answer.",
			Tools =
			[
				AIFunctionFactory.Create(() =>
				{
					Interlocked.Increment(ref keyCalls);
					return key;
				}, "get_lookup_key", "Returns the private key required by lookup_answer."),
				AIFunctionFactory.Create((string lookupKey) =>
				{
					Assert.True(Volatile.Read(ref keyCalls) > 0, "The key tool must run before the answer tool.");
					Assert.Equal(key, lookupKey);
					Interlocked.Increment(ref answerCalls);
					return "cobalt";
				}, "lookup_answer", "Returns the answer for the exact key obtained from get_lookup_key.")
			]
		};
		using var nativeOptions = client.ToNative(options, timeout.Token);
		using var nativeInstructions = AppleIntelligenceChatClient.ToNative(new ChatMessage(ChatRole.System, options.Instructions));
		using var nativePrompt = AppleIntelligenceChatClient.ToNative(new ChatMessage(ChatRole.User,
			"Call get_lookup_key, then call lookup_answer using the exact returned key. " +
			"After both tools complete, reply with two lines: the key, then the answer."));
		using var nativeClient = new ChatClientNative();
		var handler = new StreamingResponseHandler(new PlainTextStreamChunker(), "apple-intelligence");
		var completion = new TaskCompletionSource<ChatResponseNative>(TaskCreationOptions.RunContinuationsAsynchronously);
		var nativeSnapshots = new List<(string? MessageId, string Text)>();
		var gate = new object();
		CancellationTokenNative? nativeToken = null;
		nativeToken = nativeClient.StreamResponse(
			[nativeInstructions, nativePrompt],
			nativeOptions,
			onUpdate: update =>
			{
				try
				{
					lock (gate)
					{
						switch (update.UpdateType)
						{
							case ResponseUpdateTypeNative.Content:
								nativeSnapshots.Add((update.MessageId, update.Text!));
								handler.ProcessContent(update.Text, update.MessageId);
								break;
							case ResponseUpdateTypeNative.ToolCall:
								handler.ProcessToolCall(update.ToolCallId, update.ToolCallName, update.ToolCallArguments);
								break;
							case ResponseUpdateTypeNative.ToolResult:
								handler.ProcessToolResult(update.ToolCallId, update.ToolCallResult);
								break;
							default:
								throw new NotSupportedException($"Unsupported update type: {update.UpdateType}");
						}
					}
				}
				catch (Exception error)
				{
					nativeToken?.Cancel();
					handler.CompleteWithError(error);
					completion.TrySetException(error);
				}
			},
			onComplete: (response, error) =>
			{
				if (error is not null || response is null)
				{
					var failure = error is not null ? (Exception)new NSErrorException(error) :
						new InvalidOperationException("StreamResponse completed without a collected response.");
					handler.CompleteWithError(failure);
					completion.TrySetException(failure);
				}
				else
				{
					lock (gate)
					{
						handler.Complete();
						completion.TrySetResult(response);
					}
				}
			});
		using var registration = timeout.Token.Register(() => nativeToken?.Cancel());
		try
		{
			using var collectedNative = await completion.Task.WaitAsync(timeout.Token);
			var collected = new ChatResponse(collectedNative.Messages.Select(AppleIntelligenceChatClient.FromNative).ToList());
			var updates = new List<ChatResponseUpdate>();
			await foreach (var update in handler.ReadAllAsync(timeout.Token))
				updates.Add(update);

			Assert.Equal(1, Volatile.Read(ref keyCalls));
			Assert.Equal(1, Volatile.Read(ref answerCalls));
			var calls = updates.SelectMany(u => u.Contents.OfType<FunctionCallContent>()).ToList();
			var results = updates.SelectMany(u => u.Contents.OfType<FunctionResultContent>()).ToList();
			Assert.Equal(new[] { "get_lookup_key", "lookup_answer" }, calls.Select(c => c.Name));
			Assert.Equal(2, results.Count);
			Assert.Equal(calls.Select(c => c.CallId), results.Select(r => r.CallId));
			Assert.All(updates.Where(u => u.Contents.Any(c => c is FunctionCallContent or FunctionResultContent)),
				u => Assert.Null(u.MessageId));

			var expected = collected.Messages
				.Where(m => m.Role == ChatRole.Assistant && m.Contents.OfType<TextContent>().Any() && m.Text.Length > 0)
				.ToDictionary(m => m.MessageId!, m => m.Text, StringComparer.Ordinal);
			Assert.NotEmpty(expected);
			var streamed = updates.Where(u => u.Contents.OfType<TextContent>().Any())
				.GroupBy(u => u.MessageId!, StringComparer.Ordinal)
				.ToDictionary(g => g.Key, g => string.Concat(g.Select(u => u.Text)), StringComparer.Ordinal);
			Assert.Equal(expected.Keys.Order(), streamed.Keys.Order());
			foreach (var (id, text) in expected)
				Assert.Equal(text, streamed[id]);
			Assert.All(nativeSnapshots, snapshot =>
			{
				Assert.NotNull(snapshot.MessageId);
				Assert.True(expected.TryGetValue(snapshot.MessageId, out var text),
					"Every native streaming ID must identify a collected response entry.");
				Assert.StartsWith(snapshot.Text, text, StringComparison.Ordinal);
			});
			foreach (var snapshots in nativeSnapshots.GroupBy(s => s.MessageId!))
				Assert.Equal(expected[snapshots.Key], snapshots.Last().Text);

			var aggregated = updates.ToChatResponse().Messages
				.Where(m => m.MessageId is not null && m.Contents.OfType<TextContent>().Any())
				.GroupBy(m => m.MessageId!, StringComparer.Ordinal)
				.ToDictionary(g => g.Key, g => string.Concat(g.Select(m => m.Text)), StringComparer.Ordinal);
			Assert.Equal(expected.Keys.Order(), aggregated.Keys.Order());
			foreach (var (id, text) in expected)
				Assert.Equal(text, aggregated[id]);
			var answer = collected.Messages.Last(m => m.Role == ChatRole.Assistant && m.Contents.OfType<TextContent>().Any());
			Assert.False(string.IsNullOrWhiteSpace(answer.Text));
			Assert.Contains(key, answer.Text, StringComparison.Ordinal);
			Assert.Contains("cobalt", answer.Text, StringComparison.OrdinalIgnoreCase);
			output.WriteLine($"System model: {nativeSnapshots.Count} content snapshots; " +
				$"{calls.Count} actual tool calls; {expected.Count} nonempty response entries; exact per-ID oracle passed.");
		}
		finally
		{
			nativeToken?.Cancel();
			nativeToken?.Dispose();
		}
	}
}
#endif
