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
	protected virtual bool RejectsStatefulConversationOptions => false;
	protected virtual bool RequiresExplicitJsonSchema => false;
	protected virtual bool SupportsReasoningEffortNone => false;

	private void ConfigureSamplingOptions(ChatOptions options)
	{
		if (SupportsReasoningEffortNone && options.Temperature is not null)
			options.Reasoning = new() { Effort = ReasoningEffort.None };
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task SchemaFreeJson_WhenExplicitSchemaRequired_RejectsBeforeInference(bool streaming)
	{
		if (!RequiresExplicitJsonSchema)
			return;

		using var client = new T();
		var options = new ChatOptions { ResponseFormat = ChatResponseFormat.Json };
		ChatMessage[] messages = [new(ChatRole.User, "Generate a JSON object.")];
		var error = await Assert.ThrowsAsync<NotSupportedException>(async () =>
		{
			if (streaming)
				await client.GetStreamingResponseAsync(messages, options).ToChatResponseAsync();
			else
				await client.GetResponseAsync(messages, options);
		});
		Assert.Contains("explicit JSON schema", error.Message, StringComparison.Ordinal);
	}

#pragma warning disable MEAI001 // Exercise the pinned MEAI continuation/background options.
	[Theory]
	[InlineData(nameof(ChatOptions.ContinuationToken), false)]
	[InlineData(nameof(ChatOptions.ContinuationToken), true)]
	[InlineData(nameof(ChatOptions.ConversationId), false)]
	[InlineData(nameof(ChatOptions.ConversationId), true)]
	[InlineData(nameof(ChatOptions.AllowBackgroundResponses), false)]
	[InlineData(nameof(ChatOptions.AllowBackgroundResponses), true)]
	public async Task UnsupportedConversationOptions_RejectBeforeInference(string optionName, bool streaming)
	{
		// Stateful providers need real identifiers; fabricated tokens do not test their supported paths.
		if (!RejectsStatefulConversationOptions)
			return;

		var options = new ChatOptions();
		if (optionName == nameof(ChatOptions.ContinuationToken))
			options.ContinuationToken = new TestContinuationToken();
		else if (optionName == nameof(ChatOptions.ConversationId))
			options.ConversationId = "";
		else
			options.AllowBackgroundResponses = true;
		using var client = new T();

		var error = await Assert.ThrowsAsync<NotSupportedException>(async () =>
		{
			if (streaming)
				await client.GetStreamingResponseAsync([new(ChatRole.User, "Hello")], options).ToChatResponseAsync();
			else
				await client.GetResponseAsync([new(ChatRole.User, "Hello")], options);
		});
		Assert.Contains(optionName, error.Message, StringComparison.Ordinal);
	}

	private sealed class TestContinuationToken : ResponseContinuationToken;
#pragma warning restore MEAI001

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetResponseAsync_WithInstructions_Succeeds()
	{
		using var client = new T();
		var response = await client.GetResponseAsync(
			[new(ChatRole.User, "Hello")],
			new ChatOptions { Instructions = "You are a helpful assistant." });

		Assert.NotNull(response);
		Assert.NotEmpty(response.Messages);
	}

	[Theory]
	[InlineData(false, false)]
	[InlineData(true, false)]
	[InlineData(false, true)]
	[InlineData(true, true)]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetResponseAsync_WithComplementarySystemInstructions_PreservesBoth(
		bool useOptionsInstructions, bool systemInMiddle)
	{
		using var client = new T();
		const string firstInstruction = "Store ALPHA as the value of slot-one.";
		var options = new ChatOptions
		{
			Instructions = useOptionsInstructions ? firstInstruction : null
		};
		var messages = new List<ChatMessage>();
		if (!useOptionsInstructions)
			messages.Add(new(ChatRole.System, firstInstruction));
		if (systemInMiddle)
		{
			messages.Add(new(ChatRole.User, "Acknowledge receipt of the instructions."));
			messages.Add(new(ChatRole.Assistant, "Acknowledged."));
		}
		messages.Add(new(ChatRole.System, "Store BETA as the value of slot-two."));
		messages.Add(new(ChatRole.User,
			"Reply with the value of slot-one followed by the value of slot-two, in that order, and no other text."));

		var response = await client.GetResponseAsync(messages, options);

		Assert.NotNull(response);
		Assert.NotEmpty(response.Messages);
		Assert.Contains("ALPHA", response.Text, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("BETA", response.Text, StringComparison.OrdinalIgnoreCase);
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
			MaxOutputTokens = 100,
			TopK = 10,
			Seed = 42
		};

		await client.GetResponseAsync(messages, options);
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public virtual async Task GetResponseAsync_WithResponseFormat_AcceptsJsonFormat()
	{
		if (RequiresExplicitJsonSchema)
			return;

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
	public virtual async Task GetResponseAsync_WithExtremeTemperature_HandlesGracefully()
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
		ConfigureSamplingOptions(options);

		await client.GetResponseAsync(messages, options);
	}

	[Fact]
	public virtual async Task GetResponseAsync_WithZeroMaxTokens_ThrowsException()
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
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public virtual async Task GetStreamingResponseAsync_WithResponseFormat_AcceptsJsonFormat()
	{
		if (RequiresExplicitJsonSchema)
			return;

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
