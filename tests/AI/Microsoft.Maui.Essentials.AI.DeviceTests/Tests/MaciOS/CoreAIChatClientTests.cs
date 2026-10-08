#if ENABLE_CORE_AI && (IOS || MACCATALYST)
using System.Diagnostics;
using System.Text.Json;
using System.Collections.Concurrent;
using Foundation;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

internal static class CoreAIModelFixture
{
	public static string DirectoryPath => Path.Combine(NSBundle.MainBundle.ResourcePath
		?? throw new InvalidOperationException("The app resource directory is unavailable."), "CoreAIModel");

	public static CoreAIChatClient Create(CoreAITestLogCollector? logger = null)
	{
		Assert.True(File.Exists(Path.Combine(DirectoryPath, "metadata.json")), "Explicit Core AI validation requires the staged model fixture.");
		return new CoreAIChatClient(DirectoryPath, logger);
	}

	public static ChatOptions Options => new() { Temperature = 0.6f, MaxOutputTokens = 1536 };

	public static string Answer(ChatResponse response) => string.Concat(response.Messages
		.Where(message => message.Role == ChatRole.Assistant)
		.SelectMany(message => message.Contents).OfType<TextContent>().Select(content => content.Text));
}

// Only the test adapter has new(); production always requires explicit local configuration.
public sealed class ConfiguredCoreAIChatClient : IChatClient
{
	private readonly CoreAIChatClient _client = CoreAIModelFixture.Create();
	public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
		=> _client.GetResponseAsync(messages, options ?? CoreAIModelFixture.Options, cancellationToken);
	public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
		=> _client.GetStreamingResponseAsync(messages, options ?? CoreAIModelFixture.Options, cancellationToken);
	public object? GetService(Type serviceType, object? serviceKey = null) => ((IChatClient)_client).GetService(serviceType, serviceKey);
	public void Dispose() => ((IDisposable)_client).Dispose();
}

public class CoreAIChatClientCommonResponseTests : ChatClientResponseTestsBase<ConfiguredCoreAIChatClient> { }
public class CoreAIChatClientCommonStreamingTests : ChatClientStreamingTestsBase<ConfiguredCoreAIChatClient> { }

[Trait("CoreAI", "true")]
[Trait(TestTraits.RequiresModel, TestTraits.True)]
public class CoreAIChatClientTests
{
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Response_ReportsLocalIdentityAndNativeSuppliedUsage(bool streaming)
	{
		using var client = CoreAIModelFixture.Create();
		using var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(CoreAIModelFixture.DirectoryPath, "metadata.json")));
		var expectedName = metadata.RootElement.GetProperty("name").GetString();
		var options = CoreAIModelFixture.Options;
		options.ModelId = "not-a-resource-path";
		var response = await Respond(client, [new(ChatRole.User, "Say hello in one short sentence.")], options, streaming);

		Assert.Equal(expectedName, client.GetService<ChatClientMetadata>()?.DefaultModelId);
		Assert.Equal("coreai", client.GetService<ChatClientMetadata>()?.ProviderName);
		Assert.Equal(expectedName, response.ModelId);
		Assert.False(string.IsNullOrWhiteSpace(CoreAIModelFixture.Answer(response)));
		Assert.NotNull(response.Usage);
		Assert.True(response.Usage.InputTokenCount > 0);
		Assert.True(response.Usage.OutputTokenCount > 0);
		Assert.Equal(response.Usage.InputTokenCount + response.Usage.OutputTokenCount, response.Usage.TotalTokenCount);
		Assert.DoesNotContain(response.Messages.SelectMany(message => message.Contents), content => content is TextReasoningContent);
	}

	[Fact]
	public async Task NativeStream_DeltasReconstructCollectedAnswerFromSameRequest()
	{
		using var native = new ChatClientNative(CoreAIModelFixture.DirectoryPath);
		var handler = new StreamingResponseHandler(new PlainTextStreamChunker());
		var completion = new TaskCompletionSource<ChatResponseNative>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var options = new ChatOptionsNative { Temperature = NSNumber.FromDouble(0.6), MaxOutputTokens = NSNumber.FromInt32(1536) };
		using var token = native.StreamResponse(
			[new ChatMessageNative { Role = ChatRoleNative.User, Contents = [new TextContentNative("Say hello in a single short sentence. /no_think")] }],
			options,
			update =>
			{
				if (update.UpdateType != ResponseUpdateTypeNative.Content)
					completion.TrySetException(new InvalidOperationException("A plain native request emitted a non-content update."));
				else handler.ProcessContent(update.Text);
			},
			(response, error) =>
			{
				if (error is not null) completion.TrySetException(new NSErrorException(error));
				else completion.TrySetResult(response!);
			});
		try
		{
			var collected = await completion.Task.WaitAsync(TimeSpan.FromMinutes(3));
			handler.SetModelId(native.ModelIdentifier);
			handler.Complete();
			var updates = new List<ChatResponseUpdate>();
			await foreach (var update in handler.ReadAllAsync(CancellationToken.None)) updates.Add(update);
			var actual = string.Concat(updates.SelectMany(update => update.Contents).OfType<TextContent>().Select(text => text.Text));
			var expected = string.Concat(collected.Messages.Where(message => message.Role == ChatRoleNative.Assistant)
				.SelectMany(message => message.Contents).OfType<TextContentNative>().Select(text => text.Text));
			Assert.False(string.IsNullOrWhiteSpace(expected));
			Assert.Equal(expected, actual);
		}
		finally { token?.Cancel(); }
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task HistoryAndInstructions_AreAppliedWithoutConversationLeakage(bool streaming)
	{
		using var first = CoreAIModelFixture.Create();
		using var second = CoreAIModelFixture.Create();
		var firstOptions = CoreAIModelFixture.Options;
		firstOptions.Instructions = "Answer briefly. Only use the secret word supplied in this conversation.";
		var secondOptions = firstOptions.Clone();
		var firstTask = Respond(first, [new(ChatRole.User, "My secret word is ORCHID."), new(ChatRole.Assistant, "Noted."),
			new(ChatRole.User, "What is my secret word?")], firstOptions, streaming);
		var secondTask = Respond(second, [new(ChatRole.User, "My secret word is COBALT."), new(ChatRole.Assistant, "Noted."),
			new(ChatRole.User, "What is my secret word?")], secondOptions, streaming);
		var responses = await Task.WhenAll(firstTask, secondTask);
		Assert.Contains("ORCHID", CoreAIModelFixture.Answer(responses[0]), StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("COBALT", CoreAIModelFixture.Answer(responses[0]), StringComparison.OrdinalIgnoreCase);
		Assert.Contains("COBALT", CoreAIModelFixture.Answer(responses[1]), StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("ORCHID", CoreAIModelFixture.Answer(responses[1]), StringComparison.OrdinalIgnoreCase);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task NativeTool_ExecutesOnceAndPreservesCallIdsAndAwaitedTraceContext(bool streaming)
	{
		const string sourceName = "CoreAIValidation";
		using var source = new ActivitySource(sourceName);
		using var listener = new ActivityListener
		{
			ShouldListenTo = source => source.Name == sourceName,
			Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
		};
		ActivitySource.AddActivityListener(listener);
		using var activity = source.StartActivity("tool-request");
		Assert.NotNull(activity);
		var logs = new CoreAITestLogCollector();
		using var client = CoreAIModelFixture.Create(logs).AsBuilder().UseFunctionInvocation().Build();
		var calls = 0;
		var expectedCode = $"ORCHID-{Guid.NewGuid():N}";
		var tool = AIFunctionFactory.Create(async (string key) =>
		{
			Assert.Equal("alpha", key);
			Assert.Equal(activity.TraceId, Activity.Current?.TraceId);
			Assert.Equal(activity.SpanId, Activity.Current?.SpanId);
			await Task.Yield();
			Assert.Equal(activity.TraceId, Activity.Current?.TraceId);
			Assert.Equal(activity.SpanId, Activity.Current?.SpanId);
			Interlocked.Increment(ref calls);
			return expectedCode;
		}, name: "LookupValidationCode", description: "Returns the current validation code for a key. Call exactly once.");
		var options = CoreAIModelFixture.Options;
		options.Tools = [tool];
		var response = await Respond(client, [new(ChatRole.User,
			"Call LookupValidationCode exactly once with key alpha, then tell me the code returned by the tool.")], options, streaming);
		Assert.Equal(1, calls);
		var contents = response.Messages.SelectMany(message => message.Contents).ToList();
		var call = Assert.Single(contents.OfType<FunctionCallContent>());
		var result = Assert.Single(contents.OfType<FunctionResultContent>());
		Assert.False(string.IsNullOrWhiteSpace(call.CallId));
		Assert.Equal(call.CallId, result.CallId);
		Assert.True(call.InformationalOnly);
		Assert.Contains(expectedCode, CoreAIModelFixture.Answer(response), StringComparison.Ordinal);
		var toolLogs = logs.Entries.Where(entry => entry.Category == typeof(CoreAIChatClient).FullName).ToList();
		Assert.NotEmpty(toolLogs);
		Assert.All(toolLogs, entry =>
		{
			Assert.Equal(activity.TraceId, entry.TraceId);
			Assert.Equal(activity.SpanId, entry.SpanId);
		});
	}

	[Fact]
	public async Task NativeToolStream_CallbackIdsMatchCollectedTranscript_AndHistoryReplays()
	{
		using var conversionClient = CoreAIModelFixture.Create();
		var conversion = Assert.IsType<FoundationModelsChatClient>(
			conversionClient.GetService<FoundationModelsChatClient>());
		var code = $"ORCHID-{Guid.NewGuid():N}";
		var calls = 0;
		var options = CoreAIModelFixture.Options;
		options.Tools = [AIFunctionFactory.Create((string key) =>
		{
			Assert.Equal("alpha", key);
			Interlocked.Increment(ref calls);
			return code;
		}, name: "LookupValidationCode", description: "Read the validation code for a key. Call exactly once.")];
		using var nativeOptions = conversion.ToNative(options, CancellationToken.None);
		using var native = new ChatClientNative(CoreAIModelFixture.DirectoryPath);
		var prompt = "Call LookupValidationCode exactly once with key alpha, then repeat the returned code. /no_think";
		var completion = new TaskCompletionSource<ChatResponseNative>(TaskCreationOptions.RunContinuationsAsynchronously);
		var handler = new StreamingResponseHandler(new PlainTextStreamChunker());
		var callbacks = new ConcurrentQueue<ResponseUpdateNative>();
		using var token = native.StreamResponse(
			[new ChatMessageNative { Role = ChatRoleNative.User, Contents = [new TextContentNative(prompt)] }],
			nativeOptions,
			update =>
			{
				callbacks.Enqueue(update);
				switch (update.UpdateType)
				{
					case ResponseUpdateTypeNative.Content:
						handler.ProcessContent(update.Text);
						break;
					case ResponseUpdateTypeNative.ToolCall:
						handler.ProcessToolCall(update.ToolCallId, update.ToolCallName, update.ToolCallArguments);
						break;
					case ResponseUpdateTypeNative.ToolResult:
						handler.ProcessToolResult(update.ToolCallId, update.ToolCallResult);
						break;
				}
			},
			(response, error) =>
			{
				if (error is not null) completion.TrySetException(new NSErrorException(error));
				else completion.TrySetResult(response!);
			});
		try
		{
			var collected = await completion.Task.WaitAsync(TimeSpan.FromMinutes(3));
			var collectedContents = collected.Messages.SelectMany(message => message.Contents).ToList();
			var canonicalCall = Assert.Single(collectedContents.OfType<FunctionCallContentNative>());
			var canonicalResult = Assert.Single(collectedContents.OfType<FunctionResultContentNative>());
			var callbackCall = Assert.Single(callbacks, update => update.UpdateType == ResponseUpdateTypeNative.ToolCall);
			var callbackResult = Assert.Single(callbacks, update => update.UpdateType == ResponseUpdateTypeNative.ToolResult);
			Assert.Equal(canonicalCall.CallId, callbackCall.ToolCallId);
			Assert.Equal(canonicalResult.CallId, callbackResult.ToolCallId);
			Assert.Equal(canonicalCall.CallId, canonicalResult.CallId);
			Assert.Equal(1, calls);

			handler.Complete();
			var updates = new List<ChatResponseUpdate>();
			await foreach (var update in handler.ReadAllAsync(CancellationToken.None)) updates.Add(update);
			var history = new List<ChatMessage> { new(ChatRole.User, prompt) };
			history.AddRange(updates.ToChatResponse().Messages);
			history.Add(new(ChatRole.User, "Repeat the validation code from the history without calling a tool. /no_think"));
			using var replay = CoreAIModelFixture.Create().AsBuilder().UseFunctionInvocation().Build();
			var response = await replay.GetResponseAsync(history, options).WaitAsync(TimeSpan.FromMinutes(3));
			Assert.Contains(code, CoreAIModelFixture.Answer(response), StringComparison.Ordinal);
			Assert.Equal(1, calls);
		}
		finally { token?.Cancel(); }
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Schema_SeparateFromTools_ProducesRequiredTypedProperties(bool streaming)
	{
		using var client = CoreAIModelFixture.Create();
		var options = CoreAIModelFixture.Options;
		options.ResponseFormat = ChatResponseFormat.ForJsonSchema<SchemaAnswer>();
		var response = await Respond(client, [new(ChatRole.User, "Return an object with city Paris and count 3.")], options, streaming);
		using var json = JsonDocument.Parse(CoreAIModelFixture.Answer(response));
		Assert.Equal("Paris", json.RootElement.GetProperty("city").GetString());
		Assert.Equal(3, json.RootElement.GetProperty("count").GetInt32());
		Assert.Equal(2, json.RootElement.EnumerateObject().Count());
	}

	[Fact]
	public async Task Cancellation_BeforeFirstRequest_AndNextRequestSucceeds()
	{
		using var client = CoreAIModelFixture.Create();
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
			client.GetResponseAsync([new(ChatRole.User, "Hello")], CoreAIModelFixture.Options, cancellation.Token));
		await AssertNextRequest(client);
	}

	[Fact]
	public async Task Cancellation_DuringColdInitialRequest_AndNextRequestSucceeds()
	{
		using var fixture = new LocalFixtureCopy();
		using var client = new CoreAIChatClient(fixture.DirectoryPath);
		using var cancellation = new CancellationTokenSource();
		var first = client.GetResponseAsync([new(ChatRole.User, "Write twenty paragraphs. /no_think")],
			CoreAIModelFixture.Options, cancellation.Token);
		await Task.Delay(50);
		cancellation.Cancel();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.WaitAsync(TimeSpan.FromMinutes(3)));
		await AssertNextRequest(client);
	}

	[Fact]
	public async Task Cancellation_DuringGeneration_AndNextRequestSucceeds()
	{
		using var client = CoreAIModelFixture.Create();
		using var cancellation = new CancellationTokenSource();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
		{
			await foreach (var update in client.GetStreamingResponseAsync(
				[new(ChatRole.User, "Write a long story of at least twenty paragraphs. /no_think")], CoreAIModelFixture.Options, cancellation.Token))
			{
				if (update.Contents.OfType<TextContent>().Any()) cancellation.Cancel();
			}
		});
		await AssertNextRequest(client);
	}

	[Fact]
	public async Task EarlyStreamDisposal_AllowsAnotherFacadeToReuseEngine()
	{
		using var first = CoreAIModelFixture.Create();
		await foreach (var update in first.GetStreamingResponseAsync([new(ChatRole.User, "Write a long story. /no_think")], CoreAIModelFixture.Options))
		{
			if (update.Contents.OfType<TextContent>().Any()) break;
		}
		using var second = CoreAIModelFixture.Create();
		await AssertNextRequest(second);
	}

	[Theory]
	[InlineData(false, false)]
	[InlineData(false, true)]
	[InlineData(true, false)]
	[InlineData(true, true)]
	public async Task InterruptedStream_AnotherFacadeCanImmediatelyGenerateSchema(bool structured, bool cancel)
	{
		using var first = CoreAIModelFixture.Create();
		using var second = CoreAIModelFixture.Create();
		for (var iteration = 0; iteration < 4; iteration++)
		{
			using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));
			var options = CoreAIModelFixture.Options;
			if (structured) options.ResponseFormat = ChatResponseFormat.ForJsonSchema<SchemaAnswer>();
			var sawContent = false;
			async Task Interrupt()
			{
				await foreach (var update in first.GetStreamingResponseAsync(
					[new(ChatRole.User, structured
						? "Return count 3 and a city string containing a long story of at least twenty paragraphs."
						: "Write a long story of at least twenty paragraphs. /no_think")], options, cancellation.Token))
				{
					if (!update.Contents.OfType<TextContent>().Any()) continue;
					sawContent = true;
					if (cancel) cancellation.Cancel();
					else break;
				}
			}
			if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(Interrupt);
			else await Interrupt();
			Assert.True(sawContent);
			await AssertSchemaNextRequest(second);
		}
	}

	[Fact]
	public async Task CancelledTool_AnotherFacadeWaitsForManagedCompletionBeforeGeneratingSchema()
	{
		using var first = CoreAIModelFixture.Create();
		using var second = CoreAIModelFixture.Create();
		using var cancellation = new CancellationTokenSource();
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var observedCancellation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var options = CoreAIModelFixture.Options;
		options.Tools = [AIFunctionFactory.Create(async (CancellationToken token) =>
		{
			entered.TrySetResult();
			try
			{
				await Task.Delay(Timeout.Infinite, token);
				return "unreachable";
			}
			catch (OperationCanceledException) when (token.IsCancellationRequested)
			{
				observedCancellation.TrySetResult();
				await release.Task.WaitAsync(TimeSpan.FromMinutes(3));
				throw;
			}
		}, name: "WaitForCancellation", description: "Wait until this request is cancelled.")];
		var response = first.GetResponseAsync([new(ChatRole.User, "Call WaitForCancellation now. /no_think")], options, cancellation.Token);
		try
		{
			await entered.Task.WaitAsync(TimeSpan.FromMinutes(3));
			cancellation.Cancel();
			await observedCancellation.Task.WaitAsync(TimeSpan.FromSeconds(10));
			var next = AssertSchemaNextRequest(second);
			Assert.NotSame(next, await Task.WhenAny(next, Task.Delay(TimeSpan.FromMilliseconds(250))));
			release.TrySetResult();
			await Assert.ThrowsAnyAsync<OperationCanceledException>(() => response);
			await next;
		}
		finally
		{
			release.TrySetResult();
			cancellation.Cancel();
		}
	}

	[Fact]
	public async Task CancellationAndDisposal_DuringAwaitedTool_DoNotUnloadBorrowedEngine()
	{
		var client = CoreAIModelFixture.Create();
		using var cancellation = new CancellationTokenSource();
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var options = CoreAIModelFixture.Options;
		options.Tools = [AIFunctionFactory.Create(async (CancellationToken token) =>
		{
			entered.TrySetResult();
			await Task.Delay(Timeout.Infinite, token);
			return "unreachable";
		}, name: "WaitForCancellation", description: "Waits until this request is cancelled.")];
		var response = client.GetResponseAsync([new(ChatRole.User, "Call WaitForCancellation now. /no_think")], options, cancellation.Token);
		try
		{
			await entered.Task.WaitAsync(TimeSpan.FromMinutes(3));
			((IDisposable)client).Dispose();
			cancellation.Cancel();
			await Assert.ThrowsAnyAsync<OperationCanceledException>(() => response);
		}
		finally
		{
			cancellation.Cancel();
			((IDisposable)client).Dispose();
		}
		using var next = CoreAIModelFixture.Create();
		await AssertNextRequest(next);
	}

	[Theory]
	[InlineData("missing")]
	[InlineData("corrupt")]
	[InlineData("no-tokenizer")]
	public async Task InvalidBundle_FailsExplicitlyWithoutNetworkFallback(string kind)
	{
		var directory = Path.Combine(FileSystem.AppDataDirectory, "CoreAIValidationErrors", kind);
		Directory.CreateDirectory(directory);
		var metadata = Path.Combine(directory, "metadata.json");
		if (kind == "corrupt") File.WriteAllText(metadata, "not-json");
		if (kind == "no-tokenizer") File.Copy(Path.Combine(CoreAIModelFixture.DirectoryPath, "metadata.json"), metadata, true);
		using var client = new CoreAIChatClient(directory);
		var error = await Assert.ThrowsAsync<NSErrorException>(() => client.GetResponseAsync([new(ChatRole.User, "Hello")], CoreAIModelFixture.Options));
		if (kind == "no-tokenizer") Assert.Contains("tokenizer", error.Message, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public async Task BrokenChatTemplate_FailsRatherThanUsingPlainTextFallback()
	{
		using var fixture = new LocalFixtureCopy();
		File.WriteAllText(Path.Combine(fixture.DirectoryPath, "tokenizer", "chat_template.jinja"), "{% unsupported_tag %}");
		using var client = new CoreAIChatClient(fixture.DirectoryPath);
		await Assert.ThrowsAsync<NSErrorException>(() =>
			client.GetResponseAsync([new(ChatRole.User, "Hello")], CoreAIModelFixture.Options));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task PostToolTemplate_FailsExplicitlyWhenExecutorArgumentsAreNotRenderable(bool streaming)
	{
		using var fixture = new LocalFixtureCopy();
		File.AppendAllText(Path.Combine(fixture.DirectoryPath, "tokenizer", "chat_template.jinja"), """

			{% for message in messages %}
			{% if message.tool_calls is defined %}
			{% for tool_entry in message.tool_calls %}
			{% for key, value in tool_entry.function.arguments.items() %}{% endfor %}
			{% endfor %}
			{% endif %}
			{% endfor %}
			""");
		using var client = new CoreAIChatClient(fixture.DirectoryPath);
		var calls = 0;
		var options = CoreAIModelFixture.Options;
		options.Tools = [AIFunctionFactory.Create((string key) =>
		{
			Assert.Equal("alpha", key);
			Interlocked.Increment(ref calls);
			return "ORCHID";
		}, name: "LookupValidationCode", description: "Read the validation code for a key.")];
		var error = await Assert.ThrowsAsync<NSErrorException>(() => Respond(client,
			[new(ChatRole.User, "Call LookupValidationCode with key alpha, then repeat the returned code.")], options, streaming));
		Assert.True(calls == 1, $"The request failed before its post-tool template: {error.Message}; calls={calls}.");
		Assert.Contains("cannot render", error.Message, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public async Task OversizedContext_FailsExplicitly()
	{
		using var client = CoreAIModelFixture.Create();
		var error = await Assert.ThrowsAsync<NSErrorException>(() => client.GetResponseAsync(
			[new(ChatRole.User, string.Join(" ", Enumerable.Repeat("context", 12000)))], CoreAIModelFixture.Options));
		Assert.Contains("context", error.Message, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public async Task TextOnlyAndUnsupportedSamplingOptions_AreRejected()
	{
		using var client = CoreAIModelFixture.Create();
		await Assert.ThrowsAsync<NotSupportedException>(() => client.GetResponseAsync(
			[new(ChatRole.User, [new DataContent(new byte[] { 1, 2, 3 }, "image/png")])]));
		foreach (var options in new ChatOptions[]
		{
			new() { TopK = 40 }, new() { TopP = 0.9f }, new() { Seed = 42 },
			new() { FrequencyPenalty = 0.5f }, new() { PresencePenalty = 0.5f },
			new() { StopSequences = ["stop"] }, new() { ToolMode = ChatToolMode.RequireAny },
		})
		{
			await Assert.ThrowsAsync<NotSupportedException>(() => client.GetResponseAsync([new(ChatRole.User, "Hello")], options));
		}
	}

	private static async Task AssertNextRequest(IChatClient client)
	{
		var response = await client.GetResponseAsync([new(ChatRole.User, "Say hello briefly. /no_think")], CoreAIModelFixture.Options);
		Assert.False(string.IsNullOrWhiteSpace(CoreAIModelFixture.Answer(response)));
	}

	private static async Task AssertSchemaNextRequest(IChatClient client)
	{
		var options = CoreAIModelFixture.Options;
		options.ResponseFormat = ChatResponseFormat.ForJsonSchema<SchemaAnswer>();
		var response = await client.GetResponseAsync([new(ChatRole.User, "Return city Paris and count 3.")], options)
			.WaitAsync(TimeSpan.FromMinutes(3));
		var answer = CoreAIModelFixture.Answer(response);
		using var json = JsonDocument.Parse(answer);
		Assert.True(json.RootElement.TryGetProperty("city", out var city), $"Missing city after interrupted generation: {answer}");
		Assert.True(json.RootElement.TryGetProperty("count", out var count), $"Missing count after interrupted generation: {answer}");
		Assert.Equal("Paris", city.GetString());
		Assert.Equal(3, count.GetInt32());
	}

	private static async Task<ChatResponse> Respond(IChatClient client, ChatMessage[] messages, ChatOptions options, bool streaming)
	{
		using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
		// Focus phase 1 on answers/tools/schema using Qwen's documented prompt dialect,
		// without implementing a managed ReasoningOptions control.
		messages[^1].Contents.Add(new TextContent("\n/no_think"));
		if (!streaming) return await client.GetResponseAsync(messages, options, timeout.Token);
		var updates = new List<ChatResponseUpdate>();
		await foreach (var update in client.GetStreamingResponseAsync(messages, options, timeout.Token)) updates.Add(update);
		return updates.ToChatResponse();
	}

	private sealed record SchemaAnswer(
		[property: System.Text.Json.Serialization.JsonPropertyName("city")] string City,
		[property: System.Text.Json.Serialization.JsonPropertyName("count")] int Count);

	private sealed class LocalFixtureCopy : IDisposable
	{
		public string DirectoryPath { get; } = Path.Combine(FileSystem.AppDataDirectory,
			"CoreAIValidationErrors", Guid.NewGuid().ToString("N"));

		public LocalFixtureCopy()
		{
			Directory.CreateDirectory(Path.Combine(DirectoryPath, "tokenizer"));
			File.Copy(Path.Combine(CoreAIModelFixture.DirectoryPath, "metadata.json"), Path.Combine(DirectoryPath, "metadata.json"));
			foreach (var path in Directory.GetFiles(Path.Combine(CoreAIModelFixture.DirectoryPath, "tokenizer")))
				File.Copy(path, Path.Combine(DirectoryPath, "tokenizer", Path.GetFileName(path)));
			using var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(DirectoryPath, "metadata.json")));
			var asset = metadata.RootElement.GetProperty("assets").GetProperty("main").GetString()!;
			// A distinct resource URL gives the cold-load test its own engine configuration,
			// without copying weights or using a network/alternate fixture.
			Directory.CreateSymbolicLink(Path.Combine(DirectoryPath, asset), Path.Combine(CoreAIModelFixture.DirectoryPath, asset));
		}

		public void Dispose() => Directory.Delete(DirectoryPath, recursive: true);
	}
}

internal sealed class CoreAITestLogCollector : ILoggerFactory
{
	public ConcurrentQueue<Entry> Entries { get; } = new();
	public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);
	public void AddProvider(ILoggerProvider provider) { }
	public void Dispose() { }
	internal sealed record Entry(string Category, ActivityTraceId? TraceId, ActivitySpanId? SpanId);

	private sealed class Logger(CoreAITestLogCollector collector, string category) : ILogger
	{
		public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
		public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Debug;
		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
		{
			if (IsEnabled(logLevel))
				collector.Entries.Enqueue(new(category, Activity.Current?.TraceId, Activity.Current?.SpanId));
		}
	}
}
#endif
