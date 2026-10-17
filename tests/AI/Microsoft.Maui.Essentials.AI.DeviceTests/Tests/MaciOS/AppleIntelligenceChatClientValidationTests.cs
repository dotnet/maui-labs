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
	// Inspects Apple's StrictSchemaTransformCache: native structured output requires even optional CLR fields to be required.
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

	// Apple's native strict-schema path forces all optional fields across repeated requests; JSON Schema generally permits omission.
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

	// Inspects Apple's native tool-registration list and its ToolMode.None short-circuit before AIFunction validation.
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

	// Inspects Apple's native callback registration list; cloud clients use request tool envelopes instead.
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
	/// Apple native callback registration requires AIFunction; its diagnostic type listing is not a cloud error contract.
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
	/// Verifies that FunctionResultContent with a CallId that doesn't match any prior
	/// FunctionCallContent is handled gracefully (empty tool name, no exception).
	/// Apple converts orphaned results to empty native tool names; cloud tool protocols may reject this history.
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
	/// is handled gracefully (empty tool name, no exception).
	/// Apple's native empty-name fallback is provider policy; cloud tool protocols may reject an unmatched call ID.
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
	/// Apple's native name lookup tolerates empty names; cloud function schemas need not accept them.
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
	/// A custom AITool subclass that is NOT an AIFunction, used to test validation.
	/// </summary>
	private sealed class UnsupportedToolForTesting : AITool;

}

#endif
