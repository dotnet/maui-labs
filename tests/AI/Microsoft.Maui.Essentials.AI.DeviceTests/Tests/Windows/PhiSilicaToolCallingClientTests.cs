#if WINDOWS
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Maui.Essentials.AI;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

public class PhiSilicaToolCallingClientTests
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
		using var client = new PhiSilicaToolCallingClient(model)
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
	public async Task GetResponseAsync_PreviousTurnCallDoesNotBlockNewRequest()
	{
		var model = new ScriptedChatClient(
			"{\"tool_name\":\"get_time\"}",
			"{}");
		var tool = AIFunctionFactory.Create(() => "12:00", "get_time", "Gets the local time.");
		using var client = new PhiSilicaToolCallingClient(model);
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
		using var client = new PhiSilicaToolCallingClient(model);

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
		using var client = new PhiSilicaToolCallingClient(model);

		var response = await client.GetResponseAsync(
			[new(ChatRole.User, "Hello.")],
			new ChatOptions { Tools = [tool], ToolMode = ChatToolMode.None });

		Assert.Equal("No tool was called.", response.Text);
		Assert.Single(model.Requests);
	}

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
		using var client = new PhiSilicaToolCallingClient(model);
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
		using var client = new PhiSilicaToolCallingClient(model)
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
		public string[]? StreamingReplies { get; init; }
		public Task? StreamContinuation { get; init; }

		public Task<ChatResponse> GetResponseAsync(
			IEnumerable<ChatMessage> messages,
			ChatOptions? options = null,
			CancellationToken cancellationToken = default)
		{
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
	}
}
#endif
