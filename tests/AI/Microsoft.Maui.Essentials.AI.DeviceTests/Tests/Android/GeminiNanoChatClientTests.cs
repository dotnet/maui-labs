#if ANDROID
#pragma warning disable MEAI001 // Verify rejection of experimental chat options.
using Microsoft.Extensions.AI;
using System.Text.Json;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

public class GeminiNanoChatClientGetServiceTests : ChatClientGetServiceTestsBase<GeminiNanoChatClient>
{
	protected override string ExpectedProviderName => "google";
	protected override string ExpectedDefaultModelId => "gemini-nano";
}

public class GeminiNanoChatClientInstantiationTests : ChatClientInstantiationTestsBase<GeminiNanoChatClient>
{
}

public class GeminiNanoChatClientMappingTests
{
	[Fact]
	public void ToNative_PrependsInstructionsAndPreservesRolesPartsAndOrder()
	{
		byte[] bytes = [1, 2, 3];
		var messages = new ChatMessage[]
		{
			new(ChatRole.System, "system"),
			new(ChatRole.User, [new TextContent("hello"), new DataContent(bytes, "image/png")]),
			new(ChatRole.Assistant, "history"),
			new(ChatRole.Tool, "tool history"),
			new(new ChatRole("custom"), "custom history"),
		};
		using var request = GeminiNanoChatClient.ToNative(messages, new() { Instructions = "options" });
		var nativeMessages = request.GetMessages();

		Assert.Equal(["system", "system", "user", "assistant", "tool", "custom"],
			nativeMessages.Select(message => message.Role));
		Assert.Equal("options", nativeMessages[0].GetParts()[0].Text);
		Assert.Equal("system", nativeMessages[1].GetParts()[0].Text);
		var parts = nativeMessages[2].GetParts();
		Assert.Equal(2, parts.Length);
		Assert.Equal("hello", parts[0].Text);
		Assert.Equal(0, parts[0].Kind);
		Assert.Equal(bytes, parts[1].GetImage());
		Assert.Equal(1, parts[1].Kind);
		Assert.Equal(5, messages.Length);
	}

	[Fact]
	public void ToNative_LeavesNativeNumericValidationToGoogle()
	{
		using var request = GeminiNanoChatClient.ToNative([new(ChatRole.User, "hello")],
			new() { Temperature = 2, TopK = -1, Seed = int.MinValue, MaxOutputTokens = int.MaxValue });

		Assert.Equal(2, request.Options.Temperature!.FloatValue());
		Assert.Equal(-1, request.Options.TopK!.IntValue());
		Assert.Equal(int.MinValue, request.Options.Seed!.IntValue());
		Assert.Equal(int.MaxValue, request.Options.MaxOutputTokens!.IntValue());
	}

	[Theory]
	[InlineData(long.MinValue)]
	[InlineData(long.MaxValue)]
	public void ToNative_SeedOutsideNativeIntegerRange_Throws(long seed)
	{
		Assert.Throws<OverflowException>(() =>
			GeminiNanoChatClient.ToNative([new(ChatRole.User, "hello")], new() { Seed = seed }));
	}

	[Fact]
	public void ToNative_EmptyMessages_Throws()
	{
		Assert.Throws<ArgumentException>(() => GeminiNanoChatClient.ToNative([], null));
	}

	[Fact]
	public void ToNative_UnsupportedContentIsRejectedInsteadOfDiscarded()
	{
		AIContent[] unsupported =
		[
			new TextReasoningContent("history"),
			new FunctionCallContent("call", "tool"),
			new FunctionResultContent("call", "result"),
			new UriContent("https://example.com/image.png", "image/png"),
			new DataContent(Array.Empty<byte>(), "image/png"),
			new DataContent(new byte[] { 1 }, "audio/wav"),
		];
		foreach (var content in unsupported)
			Assert.Throws<NotSupportedException>(() =>
				GeminiNanoChatClient.ToNative([new(ChatRole.User, [content])], null));
	}

	[Fact]
	public void ToNative_FullThinkingMapsToNativeFlag()
	{
		using var request = GeminiNanoChatClient.ToNative([new(ChatRole.User, "hello")],
			new() { Reasoning = new() { Output = ReasoningOutput.Full } });

		Assert.True(request.Options.EnableThinking);
	}
}

public class GeminiNanoChatClientOptionsTests
{
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task JsonAndSchemaFormatsAreRejectedBeforeInference(bool streaming)
	{
		using IChatClient client = new GeminiNanoChatClient();
		foreach (var format in new[]
		{
			ChatResponseFormat.Json,
			ChatResponseFormat.ForJsonSchema(JsonSerializer.Deserialize<JsonElement>("""{"type":"object"}""")),
		})
		{
			var options = new ChatOptions { ResponseFormat = format };
			await Assert.ThrowsAsync<NotSupportedException>(async () =>
			{
				if (streaming)
				{
					await foreach (var _ in client.GetStreamingResponseAsync([new(ChatRole.User, "hello")], options)) { }
				}
				else
					await client.GetResponseAsync([new(ChatRole.User, "hello")], options);
			});
		}
	}

	[Fact]
	public void ToNative_UnsupportedOptionsAreRejected()
	{
		var tool = AIFunctionFactory.Create(() => "result");
		ChatOptions[] unsupported =
		[
			new() { Tools = [tool] },
			new() { Tools = [tool], ToolMode = ChatToolMode.None },
			new() { ToolMode = ChatToolMode.Auto },
			new() { AllowMultipleToolCalls = false },
			new() { TopP = 0.5f },
			new() { FrequencyPenalty = 0 },
			new() { PresencePenalty = 0 },
			new() { ModelId = "custom" },
			new() { ConversationId = "session" },
			new() { AllowBackgroundResponses = true },
			new() { StopSequences = ["stop"] },
			new() { AdditionalProperties = new() { ["responseSchema"] = "schema" } },
			new() { Reasoning = new() { Effort = ReasoningEffort.Medium } },
			new() { Reasoning = new() { Output = ReasoningOutput.Summary } },
		];
		foreach (var options in unsupported)
			Assert.Throws<NotSupportedException>(() =>
				GeminiNanoChatClient.ToNative([new(ChatRole.User, "hello")], options));
	}

	[Fact]
	public void ToNative_TextAndNoToolModeAreSupported()
	{
		using var request = GeminiNanoChatClient.ToNative([new(ChatRole.User, "hello")],
			new() { ResponseFormat = ChatResponseFormat.Text, ToolMode = ChatToolMode.None });

		Assert.Single(request.GetMessages());
	}
}

public class GeminiNanoChatClientResponseTests : ChatClientResponseTestsBase<GeminiNanoChatClient>
{
}

public class GeminiNanoChatClientStreamingTests : ChatClientStreamingTestsBase<GeminiNanoChatClient>
{
}

#endif
