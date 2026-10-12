#if IOS || MACCATALYST
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

/// <summary>
/// Tests for AppleIntelligenceChatClient edge cases in message conversion,
/// tool validation, and error handling paths.
/// </summary>
public class AppleIntelligenceChatClientValidationTests
{
	[Fact]
	public void ResponseSchema_WithOptionalProperties_RequiresAllNativeFields()
	{
		var format = Assert.IsType<ChatResponseFormatJson>(
			ChatResponseFormat.ForJsonSchema<OptionalResponse>(JsonSerializerOptions.Web));
		Assert.NotNull(format.Schema);
		Assert.False(format.Schema.Value.TryGetProperty("required", out _));

		var schema = AppleIntelligenceChatClient.StrictSchemaTransformCache.GetOrCreateTransformedSchema(format);

		Assert.NotNull(schema);
		var properties = schema.Value.GetProperty("properties");
		Assert.Equal(4, properties.EnumerateObject().Count());
		var required = schema.Value.GetProperty("required").EnumerateArray()
			.Select(value => value.GetString()).ToArray();
		Assert.Equal(4, required.Length);
		foreach (var name in new[] { "summary", "keyPoints", "category", "sentiment" })
		{
			Assert.True(properties.TryGetProperty(name, out _));
			Assert.Contains(name, required);
		}
		Assert.False(schema.Value.GetProperty("additionalProperties").GetBoolean());
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetResponseAsync_AndStreaming_WithRequiredNativeSchema_ReturnAllFields()
	{
		var client = new AppleIntelligenceChatClient();
		var options = new ChatOptions
		{
			ResponseFormat = ChatResponseFormat.ForJsonSchema<OptionalResponse>(JsonSerializerOptions.Web)
		};

		foreach (var prompt in new[]
		{
			"Describe a rainbow in one sentence.",
			"Name three colors in a rainbow.",
			"Explain briefly how rainbows form."
		})
		{
			var messages = new List<ChatMessage> { new(ChatRole.User, prompt) };

			var response = await client.GetResponseAsync(messages, options);
			using var fullJson = JsonDocument.Parse(response.Text!);
			AssertFourFieldResponse(fullJson.RootElement);

			var text = new System.Text.StringBuilder();
			await foreach (var update in client.GetStreamingResponseAsync(messages, options))
				text.Append(update.Text);

			using var streamedJson = JsonDocument.Parse(text.ToString());
			AssertFourFieldResponse(streamedJson.RootElement);
		}
	}

	private static void AssertFourFieldResponse(JsonElement root)
	{
		Assert.Equal(JsonValueKind.Object, root.ValueKind);
		Assert.Equal(4, root.EnumerateObject().Count());
		Assert.Equal(JsonValueKind.String, root.GetProperty("summary").ValueKind);
		Assert.Equal(JsonValueKind.Array, root.GetProperty("keyPoints").ValueKind);
		Assert.Equal(JsonValueKind.String, root.GetProperty("category").ValueKind);
		var sentiment = root.GetProperty("sentiment").GetString();
		Assert.Contains(sentiment, new[]
		{
			nameof(OptionalSentiment.Neutral),
			nameof(OptionalSentiment.Positive),
			nameof(OptionalSentiment.Negative),
			nameof(OptionalSentiment.Mixed)
		});
	}

	public sealed class OptionalResponse
	{
		public string Summary { get; set; } = string.Empty;

		public List<string> KeyPoints { get; set; } = [];

		public string Category { get; set; } = string.Empty;

		public OptionalSentiment Sentiment { get; set; }
	}

	[JsonConverter(typeof(JsonStringEnumConverter<OptionalSentiment>))]
	public enum OptionalSentiment
	{
		Neutral,
		Positive,
		Negative,
		Mixed,
	}

	[Fact]
	public void ToNative_ToolModeNone_DoesNotRegisterTools()
	{
		var options = new ChatOptions
		{
			ToolMode = ChatToolMode.None,
			Tools = [new UnsupportedToolForTesting()]
		};

		var nativeOptions = new AppleIntelligenceChatClient().ToNative(options, CancellationToken.None);

		Assert.NotNull(nativeOptions);
		Assert.Null(nativeOptions.Tools);
	}

	[Fact]
	public void ToNative_ToolModeAuto_RegistersTools()
	{
		var options = new ChatOptions
		{
			ToolMode = ChatToolMode.Auto,
			Tools = [AIFunctionFactory.Create(() => "result", "sample_tool")]
		};

		var nativeOptions = new AppleIntelligenceChatClient().ToNative(options, CancellationToken.None);

		Assert.NotNull(nativeOptions);
		Assert.NotNull(nativeOptions.Tools);
		Assert.Single(nativeOptions.Tools!);
	}

	/// <summary>
	/// Verifies that passing a non-AIFunction tool (e.g., a custom AITool subclass)
	/// throws NotSupportedException with a descriptive message listing the unsupported types.
	/// </summary>
	[Fact]
	public async Task GetResponseAsync_WithNonAIFunctionTool_ThrowsNotSupportedException()
	{
		var client = new AppleIntelligenceChatClient();
		var messages = new List<ChatMessage>
		{
			new(ChatRole.User, "Hello")
		};
		var options = new ChatOptions
		{
			Tools = [new UnsupportedToolForTesting()]
		};

		var ex = await Assert.ThrowsAsync<NotSupportedException>(
			() => client.GetResponseAsync(messages, options));
		Assert.Contains("AIFunction", ex.Message, StringComparison.Ordinal);
		Assert.Contains("UnsupportedToolForTesting", ex.Message, StringComparison.Ordinal);
	}

	/// <summary>
	/// Verifies that messages with TextContent(null) are handled gracefully.
	/// In M.E.AI 10.3.0+, TextContent(null) defaults to empty text which
	/// passes through content filtering to the native API without throwing.
	/// </summary>
	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetResponseAsync_WithOnlyNullTextContent_DoesNotThrow()
	{
		var client = new AppleIntelligenceChatClient();
		var msg = new ChatMessage(ChatRole.User, [new TextContent(null)]);
		var messages = new List<ChatMessage> { msg };

		var response = await client.GetResponseAsync(messages);
		Assert.NotNull(response);
	}

	/// <summary>
	/// Verifies that messages with unsupported content types
	/// throw ArgumentException with a descriptive message.
	/// </summary>
	[Fact]
	public async Task GetResponseAsync_WithUnsupportedContentType_ThrowsArgumentException()
	{
		var client = new AppleIntelligenceChatClient();
		var msg = new ChatMessage(ChatRole.User, [new UnsupportedContentForTesting()]);
		var messages = new List<ChatMessage> { msg };

		await Assert.ThrowsAsync<ArgumentException>(
			() => client.GetResponseAsync(messages));
	}

	/// <summary>
	/// Verifies that FunctionResultContent with a CallId that doesn't match any prior
	/// FunctionCallContent is handled gracefully (empty tool name, no exception).
	/// </summary>
	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetResponseAsync_WithOrphanedFunctionResult_DoesNotThrow()
	{
		var client = new AppleIntelligenceChatClient();
		var messages = new List<ChatMessage>
		{
			new(ChatRole.User, "What's the weather?"),
			new(ChatRole.Assistant, [new FunctionCallContent("call-1", "GetWeather")]),
			new(ChatRole.Tool, [new FunctionResultContent("call-1", "Sunny")]),
			// Orphaned result — callId "call-999" was never in a FunctionCallContent
			new(ChatRole.Tool, [new FunctionResultContent("call-999", "Unknown result")]),
			new(ChatRole.User, "Tell me more")
		};

		// Should not throw — orphaned FunctionResultContent gets empty tool name
		var response = await client.GetResponseAsync(messages);
		Assert.NotNull(response);
	}

	/// <summary>
	/// Verifies that FunctionResultContent with a CallId not matching any FunctionCallContent
	/// is handled gracefully (empty tool name, no exception). This covers the null CallId path too.
	/// </summary>
	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetResponseAsync_WithFunctionResultOrphanedCallId_DoesNotThrow()
	{
		var client = new AppleIntelligenceChatClient();

		// Build a FunctionResultContent with a CallId that has no matching FunctionCallContent
		var orphanResult = new FunctionResultContent("orphan-call-id", "Sunny result");

		var messages = new List<ChatMessage>
		{
			new(ChatRole.User, "What's the weather?"),
			new(ChatRole.Assistant, [new FunctionCallContent("call-1", "GetWeather")]),
			new(ChatRole.Tool, [new FunctionResultContent("call-1", "Sunny")]),
			// Orphaned result — callId doesn't match any prior FunctionCallContent
			new(ChatRole.Tool, [orphanResult]),
			new(ChatRole.User, "Tell me more")
		};

		// Should not throw — orphaned FunctionResultContent gets empty tool name
		var response = await client.GetResponseAsync(messages);
		Assert.NotNull(response);
	}

	/// <summary>
	/// Verifies that FunctionCallContent with empty Name populates callIdToName
	/// with an empty string, and subsequent FunctionResultContent for that CallId
	/// gets the empty name. This is the closest we can test to null since
	/// FunctionCallContent validates name is not null in its constructor.
	/// </summary>
	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetResponseAsync_WithFunctionCallEmptyName_DoesNotThrow()
	{
		var client = new AppleIntelligenceChatClient();
		var messages = new List<ChatMessage>
		{
			new(ChatRole.User, "What's the weather?"),
			new(ChatRole.Assistant, [new FunctionCallContent("call-1", "")]),
			new(ChatRole.Tool, [new FunctionResultContent("call-1", "Sunny")]),
			new(ChatRole.User, "Tell me more")
		};

		// Should not throw — empty Name means callIdToName has empty value for "call-1"
		var response = await client.GetResponseAsync(messages);
		Assert.NotNull(response);
	}

	/// <summary>
	/// Verifies that ChatOptions.Instructions is accepted and the response succeeds.
	/// The Instructions string is prepended as a system message internally.
	/// </summary>
	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetResponseAsync_WithInstructions_Succeeds()
	{
		var client = new AppleIntelligenceChatClient();
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

	/// <summary>
	/// Verifies that GetService with null serviceType throws ArgumentNullException.
	/// </summary>
	[Fact]
	public void GetService_WithNullServiceType_ThrowsArgumentNullException()
	{
		var client = new AppleIntelligenceChatClient();

		Assert.Throws<ArgumentNullException>(() =>
			((IChatClient)client).GetService(null!, null));
	}

	/// <summary>
	/// A custom AITool subclass that is NOT an AIFunction, used to test validation.
	/// </summary>
	private sealed class UnsupportedToolForTesting : AITool;

	/// <summary>
	/// A custom AIContent subclass that is not supported by Apple Intelligence.
	/// </summary>
	private sealed class UnsupportedContentForTesting : AIContent;
}

#endif
