#if ANDROID
#pragma warning disable MEAI001 // Exercise the pinned MEAI background/continuation options.
using System.Text.Json;
using AndroidNative = Com.Microsoft.Maui.Essentials.AI;
using Microsoft.Extensions.AI;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

[CollectionDefinition(nameof(GeminiNanoCollection), DisableParallelization = true)]
public class GeminiNanoCollection;

[Collection(nameof(GeminiNanoCollection))]
public class GeminiNanoChatClientGetServiceTests : ChatClientGetServiceTestsBase<GeminiNanoChatClient>
{
	protected override string ExpectedProviderName => "google";
	protected override string ExpectedDefaultModelId => "gemini-nano";
}

[Collection(nameof(GeminiNanoCollection))]
public class GeminiNanoChatClientInstantiationTests : ChatClientInstantiationTestsBase<GeminiNanoChatClient>;

[Collection(nameof(GeminiNanoCollection))]
public class GeminiNanoChatClientCancellationTests : ChatClientCancellationTestsBase<GeminiNanoChatClient>;

[Collection(nameof(GeminiNanoCollection))]
public class GeminiNanoChatClientMessagesTests : ChatClientMessagesTestsBase<GeminiNanoChatClient>
{
	// ML Kit needs conversation content and cannot consume the other standard MEAI content kinds.
	protected override bool RequiresPrompt => true;
	protected override bool RejectsUnsupportedMedia => true;
	protected override bool SupportsBinaryMedia(string mediaType) =>
		string.Equals(mediaType, "image/png", StringComparison.OrdinalIgnoreCase);
	protected override bool RejectsUnsupportedStandardContent(string kind, ChatRole role) => true;
	protected override bool RejectsMisplacedFunctionContent(ChatRole role, bool isCall) => true;
	protected override bool SupportsTrailingSystemInstruction => true;
}

[Collection(nameof(GeminiNanoCollection))]
public class GeminiNanoChatClientOptionsTests : ChatClientOptionsTestsBase<GeminiNanoChatClient>
{
	protected override bool RejectsStatefulConversationOptions => true;
	protected override bool SupportsReasoningEffortNone => true;

	// The untyped ML Kit Prompt API adapter has no JSON guarantee, including schema-free JSON.
	[Fact]
	public override Task GetResponseAsync_WithResponseFormat_AcceptsJsonFormat() => RejectJsonAsync(false);

	// The same provider policy applies to streaming requests.
	[Fact]
	public override Task GetStreamingResponseAsync_WithResponseFormat_AcceptsJsonFormat() => RejectJsonAsync(true);

	internal static async Task RejectJsonAsync(bool streaming)
	{
		using var client = new GeminiNanoChatClient();
		var options = new ChatOptions { ResponseFormat = ChatResponseFormat.Json };
		var error = await Assert.ThrowsAsync<NotSupportedException>(() => streaming
			? client.GetStreamingResponseAsync([new(ChatRole.User, "Return JSON.")], options).ToChatResponseAsync()
			: client.GetResponseAsync([new(ChatRole.User, "Return JSON.")], options));
		Assert.Contains("JSON", error.Message, StringComparison.Ordinal);
	}

	// ML Kit's temperature range is 0...1 rather than OpenAI's 0...2.
	[Fact]
	public override async Task GetResponseAsync_WithExtremeTemperature_HandlesGracefully()
	{
		using var client = new GeminiNanoChatClient();
		await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.GetResponseAsync(
			[new(ChatRole.User, "Hello")], new ChatOptions { Temperature = 2 }));
	}
}

[Collection(nameof(GeminiNanoCollection))]
public class GeminiNanoChatClientResponseTests : ChatClientResponseTestsBase<GeminiNanoChatClient>;

[Collection(nameof(GeminiNanoCollection))]
public class GeminiNanoChatClientStreamingTests : ChatClientStreamingTestsBase<GeminiNanoChatClient>;

[Collection(nameof(GeminiNanoCollection))]
public class GeminiNanoChatClientUsageTests(ITestOutputHelper output) : ChatClientUsageTestsBase<GeminiNanoChatClient>(output)
{
	// ML Kit countTokens supplies input usage only; it has no output usage report.
	protected override bool IsOutputUsageAvailable => false;
}

[Collection(nameof(GeminiNanoCollection))]
public class GeminiNanoChatClientJsonSchemaTests : ChatClientJsonSchemaTestsBase<GeminiNanoChatClient>
{
	// The adapter deliberately rejects structured output rather than pretending a prompt is a schema guarantee.
	protected override bool SupportsStructuredOutput => false;

	// ML Kit's untyped Prompt API adapter also rejects schema-free JSON.
	[Fact]
	public override Task GetResponseAsync_WithJsonFormatWithoutSchema_DoesNotThrow() =>
		GeminiNanoChatClientOptionsTests.RejectJsonAsync(false);

	// Streaming cannot supply a stronger JSON guarantee.
	[Fact]
	public override Task GetStreamingResponseAsync_WithJsonFormatWithoutSchema_DoesNotThrow() =>
		GeminiNanoChatClientOptionsTests.RejectJsonAsync(true);

	// MEAI's opt-in prompt fallback requests schema-free JSON, not plain text.
	[Fact]
	public override async Task GetResponseAsync_WithTypedPromptFallback_UsesSchemaFreeJson()
	{
		using var client = new GeminiNanoChatClient();
		await Assert.ThrowsAsync<NotSupportedException>(() => client.GetResponseAsync<SimpleResponse>(
			[new ChatMessage(ChatRole.User, "Return hello in the message field.")],
			useJsonSchemaResponseFormat: false));
	}
}

public class GeminiNanoChatClientInteropTests
{
	private const string Png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR4nGNgYPgPAAEDAQAIicLsAAAAAElFTkSuQmCC";

	// Verify JNI DTO ordering/copying and ML Kit's separate system slot without requiring AICore.
	[Fact]
	public void ToNative_PrependsInstructionsAndPreservesMarshalledPartsWithoutMutatingOrReenumerating()
	{
		var bytes = Convert.FromBase64String(Png);
		ChatMessage[] messages =
		[
			new(ChatRole.System, "system"),
			new(ChatRole.User, [new CustomContent(), new TextContent(null), new TextContent("hello"),
				new DataContent(bytes, "image/png"), new TextContent("after image"), new CustomContent()]),
			new(ChatRole.Assistant, []),
			new(ChatRole.User, [new CustomContent()]),
			new(new ChatRole("developer"), [new HostedFileContent("ignored")]),
		];
		var enumerations = 0;
		IEnumerable<ChatMessage> Enumerate()
		{
			Assert.Equal(1, ++enumerations);
			foreach (var message in messages) yield return message;
		}
		var options = new ChatOptions { Instructions = "options" };
		using var request = GeminiNanoChatClient.ToNative(Enumerate(), options);
		var native = request.GetMessages();
		Assert.Equal(["system", "system", "user", "assistant", "user"], native.Select(message => message.Role));
		Assert.Equal("options", native[0].GetParts()[0].Text);
		Assert.Equal("system", native[1].GetParts()[0].Text);
		var parts = native[2].GetParts();
		Assert.Equal(["", "hello", null, "after image"], parts.Select(part => part.Text));
		Assert.Equal([0, 0, 1, 0], parts.Select(part => part.Kind));
		Assert.Equal(bytes, parts[2].GetImage());
		Assert.Empty(native[3].GetParts());
		Assert.Empty(native[4].GetParts());
		Assert.Equal(6, messages[1].Contents.Count);
		Assert.Equal(5, messages.Length);
		Assert.Equal("options", options.Instructions);
	}

	// The bridge consumes JSON replay text rather than registering native tools. Never invent result names.
	[Fact]
	public void ToNative_ToolReplayRetainsIdsNamesArgumentsResultsAndEncounterOrder()
	{
		using var request = GeminiNanoChatClient.ToNative(
		[
			new(ChatRole.Assistant, [new TextContent("before"),
				new FunctionCallContent("call-1", "actual-name", new Dictionary<string, object?> { ["x"] = 23 }),
				new FunctionCallContent("call-2", "second-name")]),
			new(ChatRole.Tool, [new FunctionResultContent("call-2", "second"),
				new FunctionResultContent("call-1", new Dictionary<string, object?> { ["value"] = 42 }),
				new FunctionResultContent("unmatched-call", null)]),
			new(ChatRole.User, "after"),
		], null);
		var messages = request.GetMessages();
		Assert.Equal(["assistant", "tool", "user"], messages.Select(message => message.Role));
		var calls = messages[0].GetParts();
		Assert.Equal("before", calls[0].Text);
		using var firstCall = JsonDocument.Parse(calls[1].Text!);
		Assert.Equal("function_call", firstCall.RootElement.GetProperty("type").GetString());
		Assert.Equal("call-1", firstCall.RootElement.GetProperty("callId").GetString());
		Assert.Equal("actual-name", firstCall.RootElement.GetProperty("name").GetString());
		Assert.Equal(23, firstCall.RootElement.GetProperty("arguments").GetProperty("x").GetInt32());
		using var secondCall = JsonDocument.Parse(calls[2].Text!);
		Assert.Equal("call-2", secondCall.RootElement.GetProperty("callId").GetString());
		Assert.Equal("second-name", secondCall.RootElement.GetProperty("name").GetString());
		Assert.Equal(JsonValueKind.Null, secondCall.RootElement.GetProperty("arguments").ValueKind);
		using var firstResult = JsonDocument.Parse(messages[1].GetParts()[0].Text!);
		Assert.Equal("function_result", firstResult.RootElement.GetProperty("type").GetString());
		Assert.Equal("call-2", firstResult.RootElement.GetProperty("callId").GetString());
		Assert.Equal("second", firstResult.RootElement.GetProperty("result").GetString());
		Assert.False(firstResult.RootElement.TryGetProperty("name", out _));
		using var secondResult = JsonDocument.Parse(messages[1].GetParts()[1].Text!);
		Assert.Equal("call-1", secondResult.RootElement.GetProperty("callId").GetString());
		Assert.Equal(42, secondResult.RootElement.GetProperty("result").GetProperty("value").GetInt32());
		using var unmatchedResult = JsonDocument.Parse(messages[1].GetParts()[2].Text!);
		Assert.Equal(["type", "callId", "result"],
			unmatchedResult.RootElement.EnumerateObject().Select(property => property.Name));
		Assert.Equal("unmatched-call", unmatchedResult.RootElement.GetProperty("callId").GetString());
		Assert.Equal(JsonValueKind.Null, unmatchedResult.RootElement.GetProperty("result").ValueKind);
	}

	// JNI role strings must be canonical even when the MEAI standard role used different casing.
	[Fact]
	public void ToNative_CanonicalizesCaseInsensitiveStandardRoles()
	{
		using var request = GeminiNanoChatClient.ToNative(
		[
			new(new ChatRole("SYSTEM"), "instructions"),
			new(new ChatRole("ASSISTANT"), "history"),
			new(new ChatRole("TOOL"), [new FunctionResultContent("call-1", "result")]),
			new(new ChatRole("USER"), "question"),
		], null);

		Assert.Equal(["system", "assistant", "tool", "user"],
			request.GetMessages().Select(message => message.Role));
	}

	// User and Assistant image input reaches Android's byte-array decoder boundary.
	[Theory]
	[InlineData("user", false)]
	[InlineData("user", true)]
	[InlineData("assistant", false)]
	[InlineData("assistant", true)]
	public void ToNative_EmptyOrMalformedImages_ThrowManagedArgumentException(string role, bool malformed)
	{
		var content = new DataContent(malformed ? new byte[] { 1, 2, 3 } : [], "image/png");

		Assert.Throws<ArgumentException>(() => GeminiNanoChatClient.ToNative(
			[new(new ChatRole(role), [content]), new(ChatRole.User, "Hello")], null));
	}

	// Validate identifiers only after the content is in its supported history role.
	[Theory]
	[InlineData("assistant", "call-id")]
	[InlineData("assistant", "call-name")]
	[InlineData("tool", "result-id")]
	public void ToNative_MalformedIdentifiers_RejectInSupportedRole(string role, string kind)
	{
		AIContent content = kind switch
		{
			"call-id" => new FunctionCallContent(" ", "sample"),
			"call-name" => new FunctionCallContent("call-1", " "),
			_ => new FunctionResultContent(" ", "result"),
		};

		Assert.Throws<ArgumentException>(() => GeminiNanoChatClient.ToNative(
			[new(new ChatRole(role), [content]), new(ChatRole.User, "Hello")], null));
	}

	// Unsupported representations take precedence over validating payloads the provider cannot consume.
	[Theory]
	[InlineData("uri")]
	[InlineData("hosted")]
	[InlineData("audio")]
	public void ToNative_UnsupportedRepresentation_ThrowsNotSupported(string kind)
	{
		AIContent content = kind switch
		{
			"uri" => new UriContent(new Uri("relative.png", UriKind.Relative), "image/png"),
			"hosted" => new HostedFileContent("file-1"),
			_ => new DataContent(Array.Empty<byte>(), "audio/wav"),
		};

		Assert.Throws<NotSupportedException>(() => GeminiNanoChatClient.ToNative(
			[new(ChatRole.User, [content])], null));
	}

	// Native system instructions accept text only, and tool replay has defined MEAI roles.
	[Theory]
	[InlineData("system", "image")]
	[InlineData("tool", "image")]
	[InlineData("tool", "text")]
	[InlineData("user", "call")]
	[InlineData("assistant", "result")]
	public void ToNative_UnsupportedNativeContentRole_Rejects(string role, string kind)
	{
		AIContent content = kind switch
		{
			"call" => new FunctionCallContent("call-1", "sample"),
			"result" => new FunctionResultContent("call-1", "result"),
			"text" => new TextContent("not a tool result"),
			_ => new DataContent(Convert.FromBase64String(Png), "image/png"),
		};
		Assert.Throws<NotSupportedException>(() => GeminiNanoChatClient.ToNative(
			[new(new ChatRole(role), [content]), new(ChatRole.User, "Hello")], null));
	}

	// ML Kit accepts image parts for Assistant history as well as User prompts.
	[Fact]
	public void ToNative_AssistantImages_PreserveOrderedTextAndImageParts()
	{
		var bytes = Convert.FromBase64String(Png);
		using var request = GeminiNanoChatClient.ToNative(
			[new(ChatRole.Assistant, [new TextContent("before"), new DataContent(bytes, "image/png"),
				new TextContent("after")])], null);

		var message = Assert.Single(request.GetMessages());
		Assert.Equal("assistant", message.Role);
		var parts = message.GetParts();
		Assert.Equal([0, 1, 0], parts.Select(part => part.Kind));
		Assert.Equal(["before", null, "after"], parts.Select(part => part.Text));
		Assert.Equal(bytes, parts[1].GetImage());
	}

	// Only the four native builder options are marshalled. Optional unsupported MEAI settings are ignored.
	[Fact]
	public void ToNative_MapsNativeOptionsAndIgnoresUnsupportedOptionalControls()
	{
		using var request = GeminiNanoChatClient.ToNative([new(ChatRole.User, "hello")], new()
		{
			Temperature = 0.5f, TopK = 12, Seed = int.MinValue, MaxOutputTokens = 128,
			TopP = 0.7f, FrequencyPenalty = 0.1f, PresencePenalty = 0.1f, ModelId = "ignored",
			StopSequences = ["stop"], AdditionalProperties = new() { ["custom"] = true },
			RawRepresentationFactory = _ => throw new InvalidOperationException("Must not invoke unsupported factory."),
			AllowBackgroundResponses = false, Reasoning = new() { Output = ReasoningOutput.Full },
		});
		Assert.Equal(0.5f, request.Options.Temperature!.FloatValue());
		Assert.Equal(12, request.Options.TopK!.IntValue());
		Assert.Equal(int.MinValue, request.Options.Seed!.IntValue());
		Assert.Equal(128, request.Options.MaxOutputTokens!.IntValue());
		Assert.True(request.Options.EnableThinking);
	}

	// Java seed is Int32. Verify the checked conversion cannot wrap the MEAI Int64 input.
	[Theory]
	[InlineData(long.MinValue)]
	[InlineData(long.MaxValue)]
	public void ToNative_SeedOutsideNativeIntegerRange_Throws(long seed) =>
		Assert.Throws<OverflowException>(() => GeminiNanoChatClient.ToNative(
			[new(ChatRole.User, "hello")], new() { Seed = seed }));

	// ML Kit has no native tool registration; Auto/None, including non-singletons, must omit definitions.
	[Fact]
	public void ToNative_OptionalToolsAreIgnoredButRequiredInvocationRejects()
	{
		foreach (var mode in new ChatToolMode?[] { null, new NoneChatToolMode(), new AutoChatToolMode() })
		{
			using var request = GeminiNanoChatClient.ToNative([new(ChatRole.User, "hello")],
				new() { Tools = [new UnsupportedTool()], ToolMode = mode, AllowMultipleToolCalls = true });
			Assert.Single(request.GetMessages());
		}
		foreach (var mode in new[] { ChatToolMode.RequireAny, ChatToolMode.RequireSpecific("actual-name") })
			Assert.Throws<NotSupportedException>(() => GeminiNanoChatClient.ToNative(
				[new(ChatRole.User, "hello")], new() { ToolMode = mode }));
	}

	// ML Kit cannot honor reasoning effort or summaries; Full is gated again by native runtime capabilities.
	[Fact]
	public void ToNative_UnsupportedThinkingOptionsReject()
	{
		foreach (var reasoning in new ReasoningOptions[]
		{
			new() { Effort = ReasoningEffort.Medium }, new() { Output = ReasoningOutput.Summary },
			new() { Effort = ReasoningEffort.None, Output = ReasoningOutput.Full },
		})
			Assert.Throws<NotSupportedException>(() => GeminiNanoChatClient.ToNative(
				[new(ChatRole.User, "hello")], new() { Reasoning = reasoning }));
	}

	// These numbers are the verified ML Kit beta4 FeatureStatus constants, including ambiguous UNAVAILABLE.
	[Theory]
	[InlineData(3, ChatModelAvailability.Available)]
	[InlineData(1, ChatModelAvailability.NotReady)]
	[InlineData(2, ChatModelAvailability.NotReady)]
	[InlineData(0, ChatModelAvailability.Unknown)]
	[InlineData(99, ChatModelAvailability.Unknown)]
	public void CreateModelStatus_MapsAvailabilityAndOnlyAdvertisesEffectiveCapabilities(int native, ChatModelAvailability expected)
	{
		foreach (var thinking in new[] { false, true })
		{
			var status = GeminiNanoChatClient.CreateModelStatus(native, thinking);
			Assert.Equal(expected, status.Availability);
			Assert.Equal(native switch
			{
				0 => "UNAVAILABLE (0): Gemini Nano is unsupported or AICore configuration is not ready.",
				1 => "DOWNLOADABLE (1)",
				2 => "DOWNLOADING (2)",
				3 => null,
				_ => $"Unknown ML Kit FeatureStatus ({native})",
			}, status.UnavailableReason);
			Assert.Equal(thinking ? 5 : 4, status.Capabilities!.Count);
			Assert.Contains(ChatModelCapability.Text, status.Capabilities);
			Assert.Contains(ChatModelCapability.Streaming, status.Capabilities);
			Assert.Contains(ChatModelCapability.ImageInput, status.Capabilities);
			Assert.Contains(ChatModelCapability.TokenUsage, status.Capabilities);
			Assert.Equal(thinking, status.Capabilities.Contains(ChatModelCapability.Reasoning));
			Assert.DoesNotContain(ChatModelCapability.ToolCalling, status.Capabilities);
			Assert.DoesNotContain(ChatModelCapability.StructuredOutput, status.Capabilities);
		}
	}

	// Static native operations must observe cancellation before allocating their JNI client.
	[Fact]
	public async Task ModelOperations_PreCancelled_Throw()
	{
		using var cts = new CancellationTokenSource();
		cts.Cancel();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => GeminiNanoChatClient.GetModelStatusAsync(cts.Token));
		await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
		{
			await foreach (var _ in GeminiNanoChatClient.DownloadModelAsync(cts.Token))
			{
			}
		});
		await Assert.ThrowsAnyAsync<OperationCanceledException>(
			() => GeminiNanoChatClient.PrepareModelAsync(cts.Token));
	}

	// Exercise the new Java-friendly status callback/cleanup on devices without requiring or downloading AICore models.
	[Fact]
	public async Task GetModelStatusAsync_ReadsFreshNativeSnapshots()
	{
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
		var first = await GeminiNanoChatClient.GetModelStatusAsync(timeout.Token);
		var second = await GeminiNanoChatClient.GetModelStatusAsync(timeout.Token);
		Assert.NotSame(first, second);
		foreach (var status in new[] { first, second })
		{
			if (status.Availability == ChatModelAvailability.Available)
				Assert.Null(status.UnavailableReason);
			else
				Assert.False(string.IsNullOrWhiteSpace(status.UnavailableReason));
			Assert.Contains(ChatModelCapability.Text, status.Capabilities!);
			Assert.Contains(ChatModelCapability.Streaming, status.Capabilities!);
			Assert.Contains(ChatModelCapability.ImageInput, status.Capabilities!);
			Assert.Contains(ChatModelCapability.TokenUsage, status.Capabilities!);
			Assert.DoesNotContain(ChatModelCapability.ToolCalling, status.Capabilities!);
			Assert.DoesNotContain(ChatModelCapability.StructuredOutput, status.Capabilities!);
		}
	}

	// Preserve ML Kit's code, unmodified message, and retry delay across the Java/managed boundary.
	[Fact]
	public void NativeErrors_PreserveCodeReasonAndRetryDelay()
	{
		using var delay = Java.Lang.Long.ValueOf(321);
		using var native = new AndroidNative.NativeChatError("native details", 5, delay);
		var error = GeminiNanoChatClient.CreateNativeException(native, default);
		Assert.IsType<InvalidOperationException>(error);
		Assert.Equal(5, error.Data["MLKitErrorCode"]);
		Assert.Equal("native details", error.Data["MLKitErrorMessage"]);
		Assert.Equal(321L, error.Data["RetryDelayMilliseconds"]);
		using var cancelled = new AndroidNative.NativeChatError("cancelled", 7, null);
		Assert.IsType<OperationCanceledException>(GeminiNanoChatClient.CreateNativeException(cancelled, default));
	}

	// ML Kit supplies a model name/finish reason and countTokens input usage, but no timestamps or response/message IDs.
	[Fact]
	public void NativeResponses_DoNotInventMetadataOrUnavailableTokenCounts()
	{
		using var native = new AndroidNative.NativeChatResponse("answer", ["thought"], 0, "actual-native-model", 12);
		var response = GeminiNanoChatClient.FromNative(native, null);
		Assert.Equal("answer", response.Text);
		Assert.Equal("actual-native-model", response.ModelId);
		Assert.Null(response.CreatedAt);
		Assert.Null(response.ResponseId);
		Assert.Null(Assert.Single(response.Messages).MessageId);
		Assert.Equal(12, response.Usage!.InputTokenCount);
		Assert.Null(response.Usage.OutputTokenCount);
		Assert.Null(response.Usage.TotalTokenCount);
		var update = GeminiNanoChatClient.CreateFinalUpdate(native);
		Assert.Equal("actual-native-model", update.ModelId);
		Assert.Null(update.CreatedAt);
		Assert.Null(update.ResponseId);
		Assert.Null(update.MessageId);
		Assert.Equal(ChatFinishReason.Stop, update.FinishReason);
		var usage = Assert.IsType<UsageContent>(Assert.Single(update.Contents)).Details;
		Assert.Equal(12, usage.InputTokenCount);
		Assert.Null(usage.OutputTokenCount);
	}

	// Candidate.finishReason is nullable in ML Kit; the bridge's absence sentinel must not invent "other".
	[Theory]
	[InlineData(int.MinValue, null)]
	[InlineData(-100, "other")]
	[InlineData(1, "length")]
	[InlineData(99, "mlkit:99")]
	public void NativeResponses_PreserveAbsentAndNativeFinishReasons(int nativeReason, string? expected)
	{
		using var native = new AndroidNative.NativeChatResponse("answer", [], nativeReason, "model", 1);
		Assert.Equal(expected, GeminiNanoChatClient.FromNative(native, null).FinishReason?.Value);
		Assert.Equal(expected, GeminiNanoChatClient.CreateFinalUpdate(native).FinishReason?.Value);
	}

	private sealed class CustomContent : AIContent;
	private sealed class UnsupportedTool : AITool;
}
#endif
