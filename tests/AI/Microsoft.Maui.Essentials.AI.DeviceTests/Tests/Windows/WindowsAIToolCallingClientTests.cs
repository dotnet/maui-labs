using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Maui.Essentials.AI;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

public class WindowsAIToolCallingClientTests
{
	[Fact]
	public async Task GetResponseAsync_InvokesToolAndContinuesWithItsResult()
	{
		var model = new ScriptedChatClient(
			"{\"tool_name\":\"calculate\"}",
			"{\"left\":2,\"right\":3}",
			"{\"tool_name\":\"none\"}",
			"The result is 5.");
		var calls = 0;
		var tool = AIFunctionFactory.Create(
			(int left, int right) => { calls++; return left + right; },
			"calculate",
			"Adds two numbers.");
		using var client = new WindowsAIToolCallingClient(model)
			.AsBuilder()
			.UseFunctionInvocation()
			.Build();

		var response = await client.GetResponseAsync(
			[new(ChatRole.User, "Add 2 and 3.")],
			new ChatOptions { Tools = [tool] });

		Assert.Equal(1, calls);
		Assert.Contains("5", response.Text);
		Assert.Equal(4, model.Requests.Count);
		Assert.Contains(model.Requests[2], message => message.Contents.Any(content => content is FunctionResultContent));
	}

	[Fact]
	public async Task GetResponseAsync_SampleBuilderOrder_InvokesSelectedFunction()
	{
		var model = new ScriptedChatClient(
			"{\"tool_name\":\"calculate\"}", "{\"left\":2,\"right\":3}",
			"{\"tool_name\":\"none\"}", "The result is 5.");
		var calls = 0;
		using var client = model.AsBuilder()
			.UseFunctionInvocation()
			.Use(inner => new WindowsAIToolCallingClient(inner))
			.Build();
		var tool = AIFunctionFactory.Create(
			(int left, int right) => { calls++; return left + right; }, "calculate");

		var response = await client.GetResponseAsync(
			[new(ChatRole.User, "Add 2 and 3.")], new ChatOptions { Tools = [tool] });

		Assert.Equal(1, calls);
		Assert.Contains("5", response.Text);
	}

#pragma warning disable MEAI001 // The image-generation middleware and tool are experimental.
	[Fact]
	public async Task GetResponseAsync_ImageMiddlewareHandlesHostedToolBeforeAdapter()
	{
		var model = new ScriptedChatClient(
			"{\"tool_name\":\"GenerateImage\"}", "{\"prompt\":\"a robot\"}",
			"{\"tool_name\":\"none\"}", "Here is the image.");
		var generator = new ScriptedImageGenerator();
		using var client = model.AsBuilder()
			.UseImageGeneration(generator)
			.UseFunctionInvocation()
			.Use(inner => new WindowsAIToolCallingClient(inner))
			.Build();

		var response = await client.GetResponseAsync(
			[new(ChatRole.User, "Generate a robot.")],
			new ChatOptions { Tools = [new HostedImageGenerationTool()] });

		Assert.Equal(1, generator.CallCount);
		Assert.Contains(response.Messages.SelectMany(message => message.Contents),
			content => content is ImageGenerationToolResultContent);
		Assert.Equal(4, model.Options.Count);
	}

	private sealed class ScriptedImageGenerator : IImageGenerator
	{
		public int CallCount { get; private set; }

		public Task<ImageGenerationResponse> GenerateAsync(
			ImageGenerationRequest request, ImageGenerationOptions? options = null,
			CancellationToken cancellationToken = default)
		{
			CallCount++;
			return Task.FromResult(new ImageGenerationResponse([new DataContent(new byte[] { 1, 2, 3 }, "image/png")]));
		}

		public object? GetService(Type serviceType, object? serviceKey = null) => null;
		public void Dispose() { }
	}
#pragma warning restore MEAI001

	[Fact]
	public async Task GetResponseAsync_PreviousTurnCallDoesNotBlockNewRequest()
	{
		var model = new ScriptedChatClient(
			"{\"tool_name\":\"get_time\"}",
			"{}");
		var tool = AIFunctionFactory.Create(() => "12:00", "get_time", "Gets the local time.");
		using var client = new WindowsAIToolCallingClient(model);
		var messages = new List<ChatMessage>
		{
			new(ChatRole.User, "What time is it?"),
			new(ChatRole.Assistant, [new FunctionCallContent("prior", "get_time")]),
			new(ChatRole.Tool, [new FunctionResultContent("prior", "11:00")]),
			new(ChatRole.User, "What time is it now?"),
		};

		var response = await client.GetResponseAsync(messages, new ChatOptions { Tools = [tool] });

		Assert.Contains(response.Messages.SelectMany(message => message.Contents),
			content => content is FunctionCallContent call && call.Name == "get_time");
	}

	[Fact]
	public async Task GetResponseAsync_InvalidArgumentsSurfaceFailure()
	{
		var model = new ScriptedChatClient("{\"tool_name\":\"calculate\"}", "not-json");
		var tool = AIFunctionFactory.Create((int left) => left, "calculate", "Returns the input.");
		using var client = new WindowsAIToolCallingClient(model);

		await Assert.ThrowsAnyAsync<System.Text.Json.JsonException>(() =>
			client.GetResponseAsync(
				[new(ChatRole.User, "Use calculate with 2.")],
				new ChatOptions { Tools = [tool] }));
	}

	[Fact]
	public async Task GetResponseAsync_NoToolModeBypassesSelection()
	{
		var model = new ScriptedChatClient("No tool was called.");
		var tool = AIFunctionFactory.Create(() => "12:00", "get_time", "Gets the local time.");
		using var client = new WindowsAIToolCallingClient(model);

		var response = await client.GetResponseAsync(
			[new(ChatRole.User, "Hello.")],
			new ChatOptions { Tools = [tool], ToolMode = ChatToolMode.None });

		Assert.Equal("No tool was called.", response.Text);
		Assert.Single(model.Requests);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task GetResponseAsync_NoFunctions_ClearsNativeToolOptionsAndPreservesOtherOptions(bool withNoneMode)
	{
		var model = new ScriptedChatClient("Direct answer.");
		var format = ChatResponseFormat.ForJsonSchema(
			System.Text.Json.JsonDocument.Parse("""{"type":"object","properties":{"answer":{"type":"string"}}}""").RootElement,
			"answer");
		var options = new ChatOptions
		{
			ToolMode = withNoneMode ? ChatToolMode.None : ChatToolMode.RequireAny,
			AllowMultipleToolCalls = true,
			ResponseFormat = format,
			MaxOutputTokens = 42
		};
		if (withNoneMode)
			options.Tools = [AIFunctionFactory.Create(() => "unused", "unused")];
		using var client = new WindowsAIToolCallingClient(model);

		var response = await client.GetResponseAsync([new(ChatRole.User, "Hello.")], options);

		Assert.Equal("Direct answer.", response.Text);
		Assert.Single(model.Options);
		Assert.Same(format, model.Options[0]!.ResponseFormat);
		Assert.Equal(42, model.Options[0]!.MaxOutputTokens);
		Assert.Equal(withNoneMode ? ChatToolMode.None : ChatToolMode.RequireAny, options.ToolMode);
		Assert.True(options.AllowMultipleToolCalls);
	}

	[Fact]
	public async Task GetResponseAsync_SelectionArgumentsAndAnswer_ClearNativeToolOptions()
	{
		var model = new ScriptedChatClient(
			"{\"tool_name\":\"calculate\"}", "{\"left\":2,\"right\":3}",
			"{\"tool_name\":\"none\"}", "The result is 5.");
		var format = ChatResponseFormat.ForJsonSchema(
			System.Text.Json.JsonDocument.Parse("""{"type":"object","properties":{"answer":{"type":"string"}}}""").RootElement,
			"answer");
		var options = new ChatOptions
		{
			Tools = [AIFunctionFactory.Create((int left, int right) => left + right, "calculate")],
			ToolMode = ChatToolMode.RequireAny,
			AllowMultipleToolCalls = true,
			ResponseFormat = format,
			MaxOutputTokens = 42
		};
		using var client = new WindowsAIToolCallingClient(model)
			.AsBuilder().UseFunctionInvocation().Build();

		var response = await client.GetResponseAsync([new(ChatRole.User, "Add 2 and 3.")], options);

		Assert.Contains("5", response.Text);
		Assert.Equal(4, model.Options.Count);
		Assert.All(model.Options.Take(3), request =>
		{
			Assert.IsType<ChatResponseFormatJson>(request!.ResponseFormat);
			Assert.Equal(0f, request.Temperature);
			Assert.Equal(1f, request.TopP);
			Assert.Equal(1, request.TopK);
			Assert.Equal(42, request.MaxOutputTokens);
		});
		Assert.Same(format, model.Options[3]!.ResponseFormat);
		Assert.Equal(42, model.Options[3]!.MaxOutputTokens);
		Assert.NotNull(options.Tools);
		Assert.Equal(ChatToolMode.RequireAny, options.ToolMode);
	}

	[Fact]
	public async Task GetStreamingResponseAsync_NoFunctionsAndNoSelection_ClearNativeOptions()
	{
		var model = new ScriptedChatClient("{\"tool_name\":\"none\"}")
		{
			StreamingReplies = ["Streaming answer."]
		};
		var tool = AIFunctionFactory.Create(() => "unused", "unused");
		using var client = new WindowsAIToolCallingClient(model);
		var directOptions = new ChatOptions
		{
			ToolMode = ChatToolMode.RequireAny,
			AllowMultipleToolCalls = false,
			MaxOutputTokens = 17
		};
		await foreach (var _ in client.GetStreamingResponseAsync([new(ChatRole.User, "Hello.")], directOptions))
		{
		}
		Assert.Single(model.Options);
		Assert.Equal(17, model.Options[0]!.MaxOutputTokens);

		var selectionOptions = new ChatOptions
		{
			Tools = [tool],
			ToolMode = ChatToolMode.Auto,
			AllowMultipleToolCalls = true,
			MaxOutputTokens = 24
		};
		await foreach (var _ in client.GetStreamingResponseAsync([new(ChatRole.User, "No tool.")], selectionOptions))
		{
		}
		Assert.Equal(3, model.Options.Count);
		Assert.IsType<ChatResponseFormatJson>(model.Options[1]!.ResponseFormat);
		Assert.Equal(24, model.Options[2]!.MaxOutputTokens);

		var noneOptions = new ChatOptions
		{
			Tools = [tool],
			ToolMode = ChatToolMode.None,
			AllowMultipleToolCalls = true,
			MaxOutputTokens = 30
		};
		await foreach (var _ in client.GetStreamingResponseAsync([new(ChatRole.User, "No tools allowed.")], noneOptions))
		{
		}
		Assert.Equal(4, model.Options.Count);
		Assert.Equal(30, model.Options[3]!.MaxOutputTokens);
	}

	[Fact]
	public async Task GetResponseAsync_UnsupportedTool_FailsInsteadOfDroppingIt()
	{
		var model = new ScriptedChatClient("Must not be requested.");
		using var client = new WindowsAIToolCallingClient(model);
		var options = new ChatOptions { Tools = [new UnsupportedTool()] };

		var error = await Assert.ThrowsAsync<NotSupportedException>(() =>
			client.GetResponseAsync([new(ChatRole.User, "Hello.")], options));
		Assert.Contains("AIFunction", error.Message);
		Assert.Empty(model.Requests);

		options.Tools = [AIFunctionFactory.Create(() => "ok", "ok"), new UnsupportedTool()];
		await Assert.ThrowsAsync<NotSupportedException>(() =>
			client.GetResponseAsync([new(ChatRole.User, "Hello.")], options));
		Assert.Empty(model.Requests);

		await Assert.ThrowsAsync<NotSupportedException>(async () =>
		{
			await foreach (var _ in client.GetStreamingResponseAsync([new(ChatRole.User, "Hello.")], options))
			{
			}
		});
		Assert.Empty(model.Requests);
	}

	private sealed class UnsupportedTool : AITool;

	[Fact]
	public async Task GetStreamingResponseAsync_NoToolSelected_ForwardsTextBeforeCompletion()
	{
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var model = new ScriptedChatClient("{\"tool_name\":\"none\"}", "Buffered fallback.")
		{
			StreamingReplies = ["First", " second."],
			StreamContinuation = release.Task,
		};
		var tool = AIFunctionFactory.Create(() => "12:00", "get_time", "Gets the local time.");
		using var client = new WindowsAIToolCallingClient(model);
		await using var updates = client.GetStreamingResponseAsync(
			[new(ChatRole.User, "Say two words without using a tool.")],
			new ChatOptions { Tools = [tool] }).GetAsyncEnumerator();

		try
		{
			Assert.Equal("First", await NextTextAsync(updates));
			Assert.False(release.Task.IsCompleted);
			release.SetResult();
			Assert.Equal(" second.", await NextTextAsync(updates));
			Assert.False(await updates.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)));
			Assert.Equal(2, model.Requests.Count);
		}
		finally
		{
			release.TrySetResult();
		}
	}

	[Fact]
	public async Task GetStreamingResponseAsync_AfterToolResult_ForwardsFinalAnswerIncrementally()
	{
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var model = new ScriptedChatClient(
			"{\"tool_name\":\"calculate\"}",
			"{\"left\":2,\"right\":3}",
			"{\"tool_name\":\"none\"}",
			"Buffered fallback.")
		{
			StreamingReplies = ["The result is ", "5."],
			StreamContinuation = release.Task,
		};
		var calls = 0;
		var tool = AIFunctionFactory.Create(
			(int left, int right) => { calls++; return left + right; },
			"calculate",
			"Adds two numbers.");
		using var client = new WindowsAIToolCallingClient(model)
			.AsBuilder()
			.UseFunctionInvocation()
			.Build();
		await using var updates = client.GetStreamingResponseAsync(
			[new(ChatRole.User, "Add 2 and 3.")],
			new ChatOptions { Tools = [tool] }).GetAsyncEnumerator();

		try
		{
			Assert.Equal("The result is ", await NextTextAsync(updates));
			Assert.Equal(1, calls);
			Assert.False(release.Task.IsCompleted);
			release.SetResult();
			Assert.Equal("5.", await NextTextAsync(updates));
			Assert.False(await updates.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)));
			Assert.Contains(model.Requests[2], message =>
				message.Contents.Any(content => content is FunctionResultContent));
		}
		finally
		{
			release.TrySetResult();
		}
	}

	private static async Task<string> NextTextAsync(IAsyncEnumerator<ChatResponseUpdate> updates)
	{
		while (await updates.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)))
		{
			if (updates.Current.Contents.OfType<TextContent>().FirstOrDefault() is { } text)
				return text.Text;
		}

		throw new Xunit.Sdk.XunitException("The streaming response ended without text.");
	}

	private sealed class ScriptedChatClient(params string[] replies) : IChatClient
	{
		private readonly Queue<string> _replies = new(replies);
		public List<IReadOnlyList<ChatMessage>> Requests { get; } = [];
		public List<ChatOptions?> Options { get; } = [];
		public string[]? StreamingReplies { get; init; }
		public Task? StreamContinuation { get; init; }

		public Task<ChatResponse> GetResponseAsync(
			IEnumerable<ChatMessage> messages,
			ChatOptions? options = null,
			CancellationToken cancellationToken = default)
		{
			CaptureOptions(options);
			Requests.Add([.. messages]);
			if (!_replies.TryDequeue(out var reply))
				throw new InvalidOperationException("The test model received an unexpected request.");

			return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, reply)));
		}

		public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
			IEnumerable<ChatMessage> messages,
			ChatOptions? options = null,
			[EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			if (StreamingReplies is { Length: > 0 } replies)
			{
				CaptureOptions(options);
				Requests.Add([.. messages]);
				foreach (var (reply, index) in replies.Select((reply, index) => (reply, index)))
				{
					if (index > 0 && StreamContinuation is { } continuation)
						await continuation.WaitAsync(cancellationToken);
					yield return new ChatResponseUpdate
					{
						Role = ChatRole.Assistant,
						Contents = [new TextContent(reply)]
					};
				}
				yield break;
			}

			var response = await GetResponseAsync(messages, options, cancellationToken);
			yield return new ChatResponseUpdate { Role = ChatRole.Assistant, Contents = [new TextContent(response.Text)] };
		}

		public object? GetService(Type serviceType, object? serviceKey = null) => null;
		public void Dispose() { }

		private void CaptureOptions(ChatOptions? options)
		{
			if (options?.Tools is { Count: > 0 } || options?.ToolMode is not null ||
				options?.AllowMultipleToolCalls is not null)
				throw new NotSupportedException("Native Phi Silica rejects tool and tool-only options.");

			Options.Add(options?.Clone());
		}
	}
}
