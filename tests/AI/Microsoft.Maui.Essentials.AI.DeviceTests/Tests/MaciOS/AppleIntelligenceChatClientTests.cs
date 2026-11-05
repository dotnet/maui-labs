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
	// Foundation Models exposes explicit Required tool modes starting in OS 27.
	protected override bool? SupportsRequiredToolMode =>
		OperatingSystem.IsIOSVersionAtLeast(27) || OperatingSystem.IsMacCatalystVersionAtLeast(27);

	protected override bool PreservesToolActivityContext => true;

	protected override bool PreservesToolCancellation => true;
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
	// Foundation Models needs a current prompt and cannot map the standard non-text/non-image content in this matrix.
	protected override bool RequiresPrompt => true;
	protected override bool RejectsUnsupportedMedia => true;
	protected override bool SupportsBinaryMedia(string mediaType) => mediaType == "image/png";
	protected override bool RejectsUnsupportedStandardContent(string kind, ChatRole role) => true;
	protected override bool RejectsMisplacedFunctionContent(ChatRole role, bool isCall) => true;

	// Foundation Models requires the final User message as its current Prompt.
	protected override void AssertMissingPromptError(ArgumentException error)
	{
		Assert.Equal("messages", error.ParamName);
		Assert.Contains("last User message", error.Message, StringComparison.Ordinal);
	}

	// Foundation Models image support requires both OS 27 and the current native model's vision capability.
	[Theory]
	[InlineData("image/png", false)]
	[InlineData("image/png", true)]
	[InlineData("application/pdf", false)]
	[InlineData("application/pdf", true)]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public override async Task GetResponseAsync_WithBinaryContentAndText_AppliesProviderPolicy(string mediaType, bool streaming)
	{
		var os27 = OperatingSystem.IsIOSVersionAtLeast(27) || OperatingSystem.IsMacCatalystVersionAtLeast(27);
		if (mediaType != "image/png" ||
			(os27 && (await AppleIntelligenceChatClient.GetModelStatusAsync()).Capabilities?.Contains(ChatModelCapability.ImageInput) == true))
		{
			await base.GetResponseAsync_WithBinaryContentAndText_AppliesProviderPolicy(mediaType, streaming);
			return;
		}

		using var client = new AppleIntelligenceChatClient();
		var error = await Assert.ThrowsAnyAsync<Exception>(async () =>
		{
			ChatMessage[] messages = [new(ChatRole.User, [new DataContent(
				Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR4nGNgYPgPAAEDAQAIicLsAAAAAElFTkSuQmCC"), mediaType)])];
			if (streaming) await client.GetStreamingResponseAsync(messages).ToChatResponseAsync();
			else await client.GetResponseAsync(messages);
		});
		if (os27)
			Assert.Contains("vision", error.Message, StringComparison.OrdinalIgnoreCase);
		else
			Assert.IsType<NotSupportedException>(error);
	}

	// The Foundation Models bridge uses the last message as its new Prompt, which must be User.
	[Fact]
	public override async Task GetResponseAsync_WithTrailingSystemMessage_AppliesInstruction()
	{
		using var client = new AppleIntelligenceChatClient();
		ChatMessage[] messages =
		[
			new(ChatRole.User, "Remember the word PINEAPPLE."),
			new(ChatRole.System, "Reply with exactly TRAILING_SYSTEM.")
		];

		var error = await Assert.ThrowsAsync<ArgumentException>(() => client.GetResponseAsync(messages));

		Assert.Equal("messages", error.ParamName);
		Assert.Contains("last User message", error.Message, StringComparison.Ordinal);
	}
}

[Collection(nameof(AppleIntelligenceCollection))]
public class AppleIntelligenceChatClientOptionsTests : ChatClientOptionsTestsBase<AppleIntelligenceChatClient>
{
	// Foundation Models reconstructs each foreground session from supplied history, with no resumable server state.
	protected override bool RejectsStatefulConversationOptions => true;
	protected override bool RequiresExplicitJsonSchema => true;

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
	protected override bool? RequiresExplicitJsonSchema => true;
}

#endif
