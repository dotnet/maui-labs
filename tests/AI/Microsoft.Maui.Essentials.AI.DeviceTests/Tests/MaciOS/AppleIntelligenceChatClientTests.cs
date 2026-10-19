#if IOS || MACCATALYST
using System.Text.Json;
using Foundation;
using Microsoft.Extensions.AI;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

// These classes share the device's single on-device model; competing suites can exhaust generation timeouts.
[CollectionDefinition(nameof(AppleIntelligenceCollection), DisableParallelization = true)]
public class AppleIntelligenceCollection
{
}

[Collection(nameof(AppleIntelligenceCollection))]
public class AppleIntelligenceChatClientCancellationTests : ChatClientCancellationTestsBase<AppleIntelligenceChatClient>
{
}

[Collection(nameof(AppleIntelligenceCollection))]
public class AppleIntelligenceChatClientFunctionCallingTestsBase : ChatClientFunctionCallingTestsBase<AppleIntelligenceChatClient>
{
}

[Collection(nameof(AppleIntelligenceCollection))]
public class AppleIntelligenceChatClientGetServiceTests : ChatClientGetServiceTestsBase<AppleIntelligenceChatClient>
{
	protected override string ExpectedProviderName => "apple";
	protected override string ExpectedDefaultModelId => "apple-intelligence";
}

[Collection(nameof(AppleIntelligenceCollection))]
public class AppleIntelligenceChatClientInstantiationTests : ChatClientInstantiationTestsBase<AppleIntelligenceChatClient>
{
}

[Collection(nameof(AppleIntelligenceCollection))]
public class AppleIntelligenceChatClientMessagesTests : ChatClientMessagesTestsBase<AppleIntelligenceChatClient>
{
	// The Foundation Models bridge uses the last message as its new Prompt, which must be User.
	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public override async Task GetResponseAsync_WithTrailingSystemMessage_AppliesInstruction()
	{
		using var client = new AppleIntelligenceChatClient();
		ChatMessage[] messages =
		[
			new(ChatRole.User, "Remember the word PINEAPPLE."),
			new(ChatRole.System, "Reply with exactly TRAILING_SYSTEM.")
		];

		var error = await Assert.ThrowsAsync<NSErrorException>(() => client.GetResponseAsync(messages));

		Assert.Equal(nameof(ChatClientNative), error.Error.Domain);
		Assert.Equal((nint)ChatClientError.InvalidRole, error.Error.Code);
		Assert.Contains("Only user messages can be prompts.", error.Error.LocalizedDescription, StringComparison.Ordinal);
	}
}

[Collection(nameof(AppleIntelligenceCollection))]
public class AppleIntelligenceChatClientOptionsTests : ChatClientOptionsTestsBase<AppleIntelligenceChatClient>
{
	// Apple's native temperature range is 0...1, unlike OpenAI's 0...2.
	[Fact]
	public override async Task GetResponseAsync_WithExtremeTemperature_HandlesGracefully()
	{
		using var client = new AppleIntelligenceChatClient();
		var error = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
			client.GetResponseAsync([new ChatMessage(ChatRole.User, "Hello")],
				new ChatOptions { Temperature = 2.0f }));
		Assert.Contains("Temperature", error.Message, StringComparison.Ordinal);
	}

	/// <summary>
	/// Apple Intelligence requires a JSON schema for structured responses.
	/// Unlike the base test, this expects an InvalidOperationException when using ChatResponseFormat.Json without a schema.
	/// </summary>
	[Fact]
	public override async Task GetResponseAsync_WithResponseFormat_AcceptsJsonFormat()
	{
		var client = new AppleIntelligenceChatClient();
		var messages = new List<ChatMessage>
		{
			new(ChatRole.User, "Generate a JSON object")
		};
		var options = new ChatOptions
		{
			ResponseFormat = ChatResponseFormat.Json
		};

		var exception = await Assert.ThrowsAsync<InvalidOperationException>(
			() => client.GetResponseAsync(messages, options));

		Assert.Contains("JSON schema", exception.Message, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// Apple Intelligence requires a JSON schema for structured responses.
	/// Unlike the base test, this expects an InvalidOperationException when using ChatResponseFormat.Json without a schema.
	/// </summary>
	[Fact]
	public override async Task GetStreamingResponseAsync_WithResponseFormat_AcceptsJsonFormat()
	{
		var client = new AppleIntelligenceChatClient();
		var messages = new List<ChatMessage>
		{
			new(ChatRole.User, "Generate a JSON object")
		};
		var options = new ChatOptions
		{
			ResponseFormat = ChatResponseFormat.Json
		};

		var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
		{
			await foreach (var update in client.GetStreamingResponseAsync(messages, options))
			{
				// Should not reach here
			}
		});

		Assert.Contains("JSON schema", exception.Message, StringComparison.OrdinalIgnoreCase);
	}
}

[Collection(nameof(AppleIntelligenceCollection))]
public class AppleIntelligenceChatClientResponseTests : ChatClientResponseTestsBase<AppleIntelligenceChatClient>
{
	// Apple's fixed on-device model ignores ModelId overrides; cloud aliases and resolved snapshot IDs have different semantics.
	[Theory]
	[InlineData(null)]
	[InlineData("caller-supplied-model")]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetResponseAsync_ModelIdMatchesMetadata_NotRequestedModel(string? requestedModelId)
	{
		using var client = new AppleIntelligenceChatClient();
		var metadata = client.GetService<ChatClientMetadata>();

		var response = await client.GetResponseAsync(
			[new ChatMessage(ChatRole.User, "Say hello")],
			new ChatOptions { ModelId = requestedModelId });

		Assert.NotNull(metadata);
		Assert.Equal("apple-intelligence", metadata.DefaultModelId);
		Assert.Equal(metadata.DefaultModelId, response.ModelId);
	}
}

[Collection(nameof(AppleIntelligenceCollection))]
public class AppleIntelligenceChatClientStreamingTests : ChatClientStreamingTestsBase<AppleIntelligenceChatClient>
{
	// Apple's fixed on-device model ID must label every update despite caller overrides; cloud model resolution can differ.
	[Theory]
	[InlineData(null)]
	[InlineData("caller-supplied-model")]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetStreamingResponseAsync_ModelIdMatchesMetadata_NotRequestedModel(string? requestedModelId)
	{
		using var client = new AppleIntelligenceChatClient();
		var metadata = client.GetService<ChatClientMetadata>();
		var updates = new List<ChatResponseUpdate>();

		await foreach (var update in client.GetStreamingResponseAsync(
			[new ChatMessage(ChatRole.User, "Say hello")],
			new ChatOptions { ModelId = requestedModelId }))
		{
			updates.Add(update);
		}

		Assert.NotNull(metadata);
		Assert.Equal("apple-intelligence", metadata.DefaultModelId);
		Assert.NotEmpty(updates);
		Assert.All(updates, update => Assert.Equal(metadata.DefaultModelId, update.ModelId));
	}

	// The Swift bridge emits structured JSON as append-only deltas and the managed client forwards them
	// unchunked. A bridge that still sent cumulative snapshots would concatenate into several JSON documents,
	// which is native-callback behavior the shared streaming contract cannot distinguish by name.
	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetStreamingResponseAsync_WithJsonSchema_ForwardsNativeAppendOnlyDeltas()
	{
		using var client = new AppleIntelligenceChatClient();
		var schema = JsonDocument.Parse("""
			{"type":"object","title":"ExactValues","properties":{"values":{"type":"array","items":{"type":"integer"}}},"required":["values"]}
			""").RootElement.Clone();
		var options = new ChatOptions
		{
			ResponseFormat = ChatResponseFormat.ForJsonSchema(schema, "ExactValues"),
			Temperature = 0
		};

		var deltas = new List<string>();
		await foreach (var update in client.GetStreamingResponseAsync(
			[new ChatMessage(ChatRole.User, "Return exactly values [123] with no other semantic variation.")], options))
		{
			deltas.AddRange(update.Contents.OfType<TextContent>().Select(content => content.Text));
		}

		Assert.True(deltas.Count >= 2, $"Expected incremental updates, got: {string.Join("|", deltas)}");
		for (var i = 1; i < deltas.Count; i++)
		{
			var previous = string.Concat(deltas.Take(i));
			Assert.False(deltas[i].StartsWith(previous, StringComparison.Ordinal),
				$"Update {i} repeats the earlier text, so it is cumulative: {string.Join("|", deltas)}");
		}

		using var document = JsonDocument.Parse(string.Concat(deltas));
		var values = document.RootElement.GetProperty("values").EnumerateArray().Select(value => value.GetInt32());
		Assert.Equal([123], values);
	}
}

[Collection(nameof(AppleIntelligenceCollection))]
public class AppleIntelligenceChatClientUsageTests(ITestOutputHelper output)
	: ChatClientUsageTestsBase<AppleIntelligenceChatClient>(output)
{
	protected override bool IsUsageAvailable
	{
		get
		{
#if IOS
			return OperatingSystem.IsIOSVersionAtLeast(27);
#elif MACCATALYST
			return OperatingSystem.IsMacCatalystVersionAtLeast(27);
#else
			return false;
#endif
		}
	}

	// Apple OS 27's native usage mapping promises cached/reasoning counts; other providers may omit these optional fields.
	protected override void AssertProviderUsage(UsageDetails usage)
	{
		Assert.NotNull(usage.CachedInputTokenCount);
		Assert.NotNull(usage.ReasoningTokenCount);
	}
}

[Collection(nameof(AppleIntelligenceCollection))]
public class AppleIntelligenceChatClientJsonSchemaTests : ChatClientJsonSchemaTestsBase<AppleIntelligenceChatClient>
{
	// Apple requires a native generation schema even for JSON mode; replace the shared acceptance check with actual rejection.
	[Fact]
	public override Task GetResponseAsync_WithJsonFormatWithoutSchema_DoesNotThrow()
	{
		return GetResponseAsync_WithJsonFormatWithoutSchema_ThrowsInvalidOperationException();
	}

	// Apple's streaming generation also requires a native schema; cloud and Windows can accept schema-free JSON mode.
	[Fact]
	public override Task GetStreamingResponseAsync_WithJsonFormatWithoutSchema_DoesNotThrow()
	{
		return GetStreamingResponseAsync_WithJsonFormatWithoutSchema_ThrowsInvalidOperationException();
	}

	// Apple's schema-free JSON rejection and diagnostic differ from providers that implement unconstrained JSON mode.
	[Fact]
	public async Task GetResponseAsync_WithJsonFormatWithoutSchema_ThrowsInvalidOperationException()
	{
		var client = new AppleIntelligenceChatClient();
		var messages = new List<ChatMessage>
		{
			new(ChatRole.User, "Generate a JSON object")
		};
		var options = new ChatOptions
		{
			ResponseFormat = ChatResponseFormat.Json
		};

		var exception = await Assert.ThrowsAsync<InvalidOperationException>(
			() => client.GetResponseAsync(messages, options));

		Assert.Contains("JSON schema", exception.Message, StringComparison.OrdinalIgnoreCase);
	}

	// Apple's native streaming entry point rejects schema-free JSON before producing any updates; this is not universal.
	[Fact]
	public async Task GetStreamingResponseAsync_WithJsonFormatWithoutSchema_ThrowsInvalidOperationException()
	{
		var client = new AppleIntelligenceChatClient();
		var messages = new List<ChatMessage>
		{
			new(ChatRole.User, "Generate a JSON object")
		};
		var options = new ChatOptions
		{
			ResponseFormat = ChatResponseFormat.Json
		};

		var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
		{
			await foreach (var update in client.GetStreamingResponseAsync(messages, options))
			{
				// Should not reach here
			}
		});

		Assert.Contains("JSON schema", exception.Message, StringComparison.OrdinalIgnoreCase);
	}
}

#endif
