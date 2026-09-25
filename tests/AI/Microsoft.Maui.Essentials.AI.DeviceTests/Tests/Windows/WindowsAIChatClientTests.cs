#if WINDOWS
using Microsoft.Extensions.AI;
using Microsoft.Maui.Essentials.AI;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

public class WindowsAIToolCallingWrappedClient : DelegatingChatClient
{
	public WindowsAIToolCallingWrappedClient() : base(new WindowsAIToolCallingClient(new WindowsAIChatClient())) { }
}

public class WindowsAIToolCallingFunctionTests : ChatClientFunctionCallingTestsBase<WindowsAIToolCallingWrappedClient>
{
	protected override IChatClient EnableFunctionCalling(WindowsAIToolCallingWrappedClient client) =>
		client.AsBuilder().UseFunctionInvocation().Build();

	[Fact(Skip = "The experimental Windows AI adapter uses structured output; informational native calls do not apply.")]
	public override Task GetStreamingResponseAsync_InformationalOnlyFunctionCalls_NotInvokedByFICC()
		=> Task.CompletedTask;

	[Fact]
	public override async Task GetResponseAsync_ChainedFunctionCalls_TimeAndWeather()
	{
		int timeCallCount = 0;
		int weatherCallCount = 0;
		string? capturedDate = null;

		var timeTool = AIFunctionFactory.Create(
			() => { timeCallCount++; return "2025-12-02 12:00:00"; },
			name: "GetCurrentTime",
			description: "Gets the current date and time. No parameters needed.");
		var weatherTool = AIFunctionFactory.Create(
			(string date) =>
			{
				weatherCallCount++;
				capturedDate = date;
				return $"{{\"date\":\"{date}\",\"condition\":\"sunny\",\"temperature\":72,\"humidity\":45}}";
			},
			name: "GetWeather",
			description: "Gets the weather forecast for a specific date. Requires the date in YYYY-MM-DD format.");
		var client = EnableFunctionCalling(new WindowsAIToolCallingWrappedClient());
		var messages = new List<ChatMessage>
		{
			new(ChatRole.System, "GetWeather requires a date parameter. If the user does not provide a specific date, " +
				"call GetCurrentTime first to get the current date."),
			new(ChatRole.User, "What's the weather like today?")
		};

		var response = await client.GetResponseAsync(messages, new ChatOptions { Tools = [timeTool, weatherTool] });

		Assert.NotNull(response);
		Assert.True(timeCallCount > 0, "GetCurrentTime should have been called");
		Assert.True(weatherCallCount > 0, "GetWeather should have been called");
		Assert.NotNull(capturedDate);
		Assert.Contains("2025-12-02", capturedDate, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public override async Task GetStreamingResponseAsync_ChainedFunctionCalls_TimeAndWeather()
	{
		int timeCallCount = 0;
		int weatherCallCount = 0;
		var timeTool = AIFunctionFactory.Create(
			() => { timeCallCount++; return "2025-12-02 12:00:00"; },
			name: "GetCurrentTime",
			description: "Gets the current date and time. No parameters needed.");
		var weatherTool = AIFunctionFactory.Create(
			(string date) =>
			{
				weatherCallCount++;
				return $"{{\"date\":\"{date}\",\"condition\":\"cloudy\",\"temperature\":68,\"humidity\":55}}";
			},
			name: "GetWeather",
			description: "Gets the weather forecast for a specific date. Requires the date in YYYY-MM-DD format.");
		var client = EnableFunctionCalling(new WindowsAIToolCallingWrappedClient());
		var messages = new List<ChatMessage>
		{
			new(ChatRole.System, "GetWeather requires a date parameter. If the user does not provide a specific date, " +
				"call GetCurrentTime first to get the current date."),
			new(ChatRole.User, "What's the weather like today?")
		};
		var updates = new List<ChatResponseUpdate>();

		await foreach (var update in client.GetStreamingResponseAsync(
			messages, new ChatOptions { Tools = [timeTool, weatherTool] }))
			updates.Add(update);

		Assert.NotEmpty(updates);
		Assert.True(timeCallCount > 0, "GetCurrentTime should have been called");
		Assert.True(weatherCallCount > 0, "GetWeather should have been called");
	}

	[Fact]
	public async Task GetResponseAsync_ChainedFunctionCalls_ProfileToOrders()
	{
		int profileCallCount = 0;
		int ordersCallCount = 0;
		var profileTool = AIFunctionFactory.Create(
			(string username) =>
			{
				profileCallCount++;
				return "{\"userId\": \"U12345\", \"name\": \"John Doe\"}";
			},
			name: "GetUserProfile",
			description: "Looks up a user profile by username. Returns userId and name.");
		var ordersTool = AIFunctionFactory.Create(
			(string userId) =>
			{
				ordersCallCount++;
				return "[{\"orderId\": \"ORD-001\", \"item\": \"Widget\"}]";
			},
			name: "GetOrderHistory",
			description: "Gets order history for a user. Requires the userId.");
		var client = EnableFunctionCalling(new WindowsAIToolCallingWrappedClient());
		var messages = new List<ChatMessage>
		{
			new(ChatRole.System, "GetOrderHistory requires a userId. Call GetUserProfile with the username to get the userId first."),
			new(ChatRole.User, "What are the recent orders for username 'johndoe'?")
		};

		var response = await client.GetResponseAsync(messages, new ChatOptions { Tools = [profileTool, ordersTool] });

		Assert.NotNull(response);
		Assert.True(profileCallCount > 0, $"GetUserProfile should be called. Got: {profileCallCount}");
		Assert.True(ordersCallCount > 0, $"GetOrderHistory should be called. Got: {ordersCallCount}");
	}
}

public class WindowsAIChatClientCancellationTests : ChatClientCancellationTestsBase<WindowsAIChatClient>
{
}
public class WindowsAIChatClientGetServiceTests : ChatClientGetServiceTestsBase<WindowsAIChatClient>
{
	protected override string ExpectedProviderName => "windows";
	protected override string ExpectedDefaultModelId => "windows-ai-language-model";
}
public class WindowsAIChatClientInstantiationTests : ChatClientInstantiationTestsBase<WindowsAIChatClient>
{
}
public class WindowsAIChatClientMessagesTests : ChatClientMessagesTestsBase<WindowsAIChatClient>
{
}
public class WindowsAIChatClientOptionsTests : ChatClientOptionsTestsBase<WindowsAIChatClient>
{
}
public class WindowsAIChatClientResponseTests : ChatClientResponseTestsBase<WindowsAIChatClient>
{
}
public class WindowsAIChatClientStreamingTests : ChatClientStreamingTestsBase<WindowsAIChatClient>
{
}
// Structured output is handled natively by WindowsAIChatClient via
// LanguageModel.GenerateStructuredJsonResponseAsync, so no wrapper is needed.
public class WindowsAIChatClientJsonSchemaTests : ChatClientJsonSchemaTestsBase<WindowsAIChatClient>
{
}
public class WindowsAIChatClientValidationTests
{
	[Fact]
	public async Task GetResponseAsync_WithNonAIFunctionTool_ThrowsNotSupportedException()
	{
		var client = new WindowsAIChatClient();
		var messages = new List<ChatMessage>
		{
			new(ChatRole.User, "Hello")
		};
		var options = new ChatOptions
		{
			Tools = [new UnsupportedToolForTesting()]
		};

		await Assert.ThrowsAsync<NotSupportedException>(
			() => client.GetResponseAsync(messages, options));
	}

	[Fact]
	public async Task GetResponseAsync_WithAIFunctionTool_ThrowsInsteadOfIgnoringIt()
	{
		using var client = new WindowsAIChatClient();
		var options = new ChatOptions
		{
			Tools = [AIFunctionFactory.Create(() => "unexpected", name: "get_time")],
		};

		var error = await Assert.ThrowsAsync<NotSupportedException>(() =>
			client.GetResponseAsync([new ChatMessage(ChatRole.User, "Hello")], options));

		Assert.Contains("does not support tool calling", error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task GetResponseAsync_WithToolMode_ThrowsInsteadOfIgnoringIt()
	{
		using var client = new WindowsAIChatClient();
		var options = new ChatOptions { ToolMode = ChatToolMode.RequireAny };

		await Assert.ThrowsAsync<NotSupportedException>(() =>
			client.GetResponseAsync([new ChatMessage(ChatRole.User, "Hello")], options));
	}

	[Fact]
	public async Task GetResponseAsync_WithImage_RejectsWithoutDescribing()
	{
		using var client = new WindowsAIChatClient();
		var message = new ChatMessage(ChatRole.User,
			[new DataContent(new byte[] { 0 }, "image/png"), new TextContent("What is shown?")]);

		var error = await Assert.ThrowsAsync<NotSupportedException>(
			() => client.GetResponseAsync([message]));

		Assert.Contains("text only", error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task GetResponseAsync_WithOnlyNullTextContent_DoesNotThrow()
	{
		var client = new WindowsAIChatClient();
		var msg = new ChatMessage(ChatRole.User, [new TextContent(null)]);
		var messages = new List<ChatMessage> { msg };

		var response = await client.GetResponseAsync(messages);
		Assert.NotNull(response);
	}

	[Fact]
	public async Task GetResponseAsync_WithUnsupportedContentType_ThrowsArgumentException()
	{
		var client = new WindowsAIChatClient();
		var msg = new ChatMessage(ChatRole.User, [new UnsupportedContentForTesting()]);
		var messages = new List<ChatMessage> { msg };

		await Assert.ThrowsAsync<ArgumentException>(
			() => client.GetResponseAsync(messages));
	}

	[Fact]
	public async Task GetResponseAsync_WithOrphanedFunctionResult_DoesNotThrow()
	{
		var client = new WindowsAIChatClient();
		var messages = new List<ChatMessage>
		{
			new(ChatRole.User, "What's the weather?"),
			new(ChatRole.Assistant, [new FunctionCallContent("call-1", "GetWeather")]),
			new(ChatRole.Tool, [new FunctionResultContent("call-1", "Sunny")]),
			new(ChatRole.Tool, [new FunctionResultContent("call-999", "Unknown result")]),
			new(ChatRole.User, "Tell me more")
		};

		var response = await client.GetResponseAsync(messages);
		Assert.NotNull(response);
	}

	[Fact]
	public async Task GetResponseAsync_WithFunctionCallEmptyName_DoesNotThrow()
	{
		var client = new WindowsAIChatClient();
		var messages = new List<ChatMessage>
		{
			new(ChatRole.User, "What's the weather?"),
			new(ChatRole.Assistant, [new FunctionCallContent("call-1", "")]),
			new(ChatRole.Tool, [new FunctionResultContent("call-1", "Sunny")]),
			new(ChatRole.User, "Tell me more")
		};

		var response = await client.GetResponseAsync(messages);
		Assert.NotNull(response);
	}

	[Fact]
	public async Task GetResponseAsync_WithInstructions_Succeeds()
	{
		var client = new WindowsAIChatClient();
		var messages = new List<ChatMessage>
		{
			new(ChatRole.User, "Hello")
		};
		var options = new ChatOptions
		{
			Instructions = "You are a helpful assistant."
		};

		var response = await client.GetResponseAsync(messages, options);
		Assert.NotNull(response);
		Assert.NotEmpty(response.Messages);
	}

	[Fact]
	public void GetService_WithNullServiceType_ThrowsArgumentNullException()
	{
		var client = new WindowsAIChatClient();

		Assert.Throws<ArgumentNullException>(() =>
			((IChatClient)client).GetService(null!, null));
	}

	private sealed class UnsupportedToolForTesting : AITool;

	private sealed class UnsupportedContentForTesting : AIContent;
}

#endif
