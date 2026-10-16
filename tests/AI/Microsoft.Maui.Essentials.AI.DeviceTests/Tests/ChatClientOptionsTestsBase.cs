using Microsoft.Extensions.AI;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

/// <summary>
/// Base class for ChatClient options tests.
/// Provides common tests for any IChatClient implementation.
/// </summary>
/// <typeparam name="T">The concrete ChatClient type to test.</typeparam>
public abstract class ChatClientOptionsTestsBase<T>
	where T : class, IChatClient, new()
{
	[Theory]
	[InlineData("top-p", false)]
	[InlineData("top-p", true)]
	[InlineData("top-k", false)]
	[InlineData("top-k", true)]
	[InlineData("temperature-only", false)]
	[InlineData("temperature-only", true)]
	[InlineData("seed-only", false)]
	[InlineData("seed-only", true)]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task SamplingHints_ReturnNonEmptyResponse(string sampling, bool streaming)
	{
		using var client = new T();
		using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
		var options = sampling switch
		{
			"top-p" => new ChatOptions { TopP = 0.9f, Temperature = 0.5f, Seed = 42 },
			"top-k" => new ChatOptions { TopK = 20, Temperature = 0.5f, Seed = 42 },
			"temperature-only" => new ChatOptions { Temperature = 0.5f },
			"seed-only" => new ChatOptions { Seed = 42 },
			_ => throw new ArgumentOutOfRangeException(nameof(sampling))
		};
		options.MaxOutputTokens = 64;
		ChatMessage[] messages = [new(ChatRole.User, "Say hello in one brief sentence.")];
		ChatResponse response;
		if (streaming)
		{
			var updates = new List<ChatResponseUpdate>();
			await foreach (var update in client.GetStreamingResponseAsync(messages, options, timeout.Token))
				updates.Add(update);
			response = updates.ToChatResponse();
		}
		else
		{
			response = await client.GetResponseAsync(messages, options, timeout.Token);
		}
		Assert.False(string.IsNullOrWhiteSpace(response.Text));
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetResponseAsync_AcceptsNullOptions()
	{
		var client = new T();
		var messages = new List<ChatMessage>
		{
			new(ChatRole.User, "Hello")
		};

		await client.GetResponseAsync(messages, options: null);
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetResponseAsync_WithChatOptions_AcceptsValidOptions()
	{
		var client = new T();
		var messages = new List<ChatMessage>
		{
			new(ChatRole.User, "Hello")
		};
		var options = new ChatOptions
		{
			Temperature = 0.7f,
			MaxOutputTokens = 100,
			TopK = 10,
			Seed = 42
		};

		await client.GetResponseAsync(messages, options);
	}

	[Fact]
	public virtual async Task GetResponseAsync_WithResponseFormat_AcceptsJsonFormat()
	{
		var client = new T();
		var messages = new List<ChatMessage>
		{
			new(ChatRole.User, "Generate a JSON object")
		};
		var options = new ChatOptions
		{
			ResponseFormat = ChatResponseFormat.Json
		};

		await client.GetResponseAsync(messages, options);
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetResponseAsync_WithExtremeTemperature_HandlesGracefully()
	{
		var client = new T();
		var messages = new List<ChatMessage>
		{
			new(ChatRole.User, "Hello")
		};

		var options = new ChatOptions
		{
			Temperature = 2.0f
		};

		await client.GetResponseAsync(messages, options);
	}

	[Fact]
	public async Task GetResponseAsync_WithZeroMaxTokens_ThrowsException()
	{
		var client = new T();
		var messages = new List<ChatMessage>
		{
			new(ChatRole.User, "Hello")
		};
		var options = new ChatOptions
		{
			MaxOutputTokens = 0
		};

		await Assert.ThrowsAnyAsync<Exception>(() => client.GetResponseAsync(messages, options));
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetStreamingResponseAsync_AcceptsNullOptions()
	{
		var client = new T();
		var messages = new List<ChatMessage>
		{
			new(ChatRole.User, "Hello")
		};

		await foreach (var update in client.GetStreamingResponseAsync(messages, options: null))
		{
			// Process updates
		}
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetStreamingResponseAsync_WithChatOptions_AcceptsValidOptions()
	{
		var client = new T();
		var messages = new List<ChatMessage>
		{
			new(ChatRole.User, "Hello")
		};
		var options = new ChatOptions
		{
			Temperature = 0.7f,
			MaxOutputTokens = 100,
			TopK = 10,
			Seed = 42
		};

		await foreach (var update in client.GetStreamingResponseAsync(messages, options))
		{
			// Process updates
		}
	}

	[Fact]
	public virtual async Task GetStreamingResponseAsync_WithResponseFormat_AcceptsJsonFormat()
	{
		var client = new T();
		var messages = new List<ChatMessage>
		{
			new(ChatRole.User, "Generate a JSON object")
		};
		var options = new ChatOptions
		{
			ResponseFormat = ChatResponseFormat.Json
		};

		await foreach (var update in client.GetStreamingResponseAsync(messages, options))
		{
			// Process updates
		}
	}
}
