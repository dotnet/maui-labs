#if IOS || MACCATALYST
using Foundation;
using Microsoft.Extensions.AI;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

[Collection(nameof(AppleIntelligenceCollection))]
public class AppleIntelligenceChatClientConformanceTests
{
	// Foundation Models has ordered inline instructions, unlike providers with a single system slot.
	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData(" \r\n\t")]
	public void ToNative_BlankOptions_OmitsOnlyOptionsInstructions(string? instructions)
	{
		var native = AppleIntelligenceChatClient.ToNative(
			[new(ChatRole.System, ""), new(ChatRole.System, " "), new(ChatRole.User, "")],
			new ChatOptions { Instructions = instructions });

		Assert.Equal(3, native.Length);
		Assert.Equal(ChatRoleNative.System, native[0].Role);
		Assert.Equal("", Assert.IsType<TextContentNative>(Assert.Single(native[0].Contents)).Text);
		Assert.Equal(" ", Assert.IsType<TextContentNative>(Assert.Single(native[1].Contents)).Text);
	}

	// Empty supported conversion must still create each Apple inline transcript role.
	[Theory]
	[InlineData("system", false)]
	[InlineData("system", true)]
	[InlineData("assistant", false)]
	[InlineData("assistant", true)]
	[InlineData("user", false)]
	[InlineData("user", true)]
	public void ToNative_UnknownOnlyOrEmptyContents_PreservesTurn(string role, bool unknownOnly)
	{
		var messages = AppleIntelligenceChatClient.ToNative(
			[new(new ChatRole(role), unknownOnly ? [new UnrecognizedContent()] : [])], null);
		var message = Assert.Single(messages);
		Assert.Equal(role, message.Role.ToString().ToLowerInvariant());
		Assert.Equal("", Assert.IsType<TextContentNative>(Assert.Single(message.Contents)).Text);
	}

	// NSNumber.Int64Value must not wrap negative MEAI seeds to Swift UInt64.
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task NegativeSeed_RejectsBeforeInference(bool streaming)
	{
		using var client = new AppleIntelligenceChatClient();
		var options = new ChatOptions { Seed = -1 };
		var error = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
		{
			if (streaming)
				await client.GetStreamingResponseAsync([new(ChatRole.User, "Hello")], options).ToChatResponseAsync();
			else
				await client.GetResponseAsync([new(ChatRole.User, "Hello")], options);
		});
		Assert.Contains("Seed", error.Message, StringComparison.Ordinal);
	}

	// Only a supplied schema selects Foundation Models constrained generation.
	[Fact]
	public void ToNative_SchemaFreeJson_UsesOrdinaryGeneration()
	{
		var native = new AppleIntelligenceChatClient().ToNative(new ChatOptions
		{
			ResponseFormat = ChatResponseFormat.Json
		}, default);

		Assert.NotNull(native);
		Assert.Null(native.ResponseJsonSchema);
	}

	// Apple OS 26 lacks explicit required modes; validate both runtime branches without needing either model.
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void ToNative_RequiredTools_OnOS26_Rejects(bool named)
	{
		var options = new ChatOptions
		{
			ToolMode = named ? ChatToolMode.RequireSpecific("first") : ChatToolMode.RequireAny,
			Tools = [AIFunctionFactory.Create(() => "first", "first")]
		};
		Assert.Throws<NotSupportedException>(() =>
			new AppleIntelligenceChatClient().ToNative(options, default, supportsOS27: false));
	}

	// The Apple bridge must restrict named Required to one tool rather than silently leaving other callbacks registered.
	[Fact]
	public void ToNative_NamedRequired_OnOS27_RegistersOnlyNamedTool()
	{
		var options = new ChatOptions
		{
			ToolMode = ChatToolMode.RequireSpecific("second"),
			Tools = [AIFunctionFactory.Create(() => "first", "first"),
				AIFunctionFactory.Create(() => "second", "second")]
		};
		var native = new AppleIntelligenceChatClient().ToNative(options, default, supportsOS27: true)!;

		Assert.Equal(ToolCallingModeNative.Required, native.ToolCallingMode);
		Assert.Equal("second", Assert.Single(native.Tools!).Name);
		Assert.Equal(2, options.Tools.Count);
	}

	// Foundation Models cannot honor a required name absent from the registered callback list.
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void ToNative_RequiredWithoutMatchingTools_Rejects(bool named)
	{
		Assert.Throws<ArgumentException>(() => new AppleIntelligenceChatClient().ToNative(
			new ChatOptions { ToolMode = named ? ChatToolMode.RequireSpecific("missing") : ChatToolMode.RequireAny },
			default, supportsOS27: true));
	}

	// OS 26 keeps its native default; OS 27 gets the explicit native allowed/disallowed/required field.
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void ToNative_OptionalModes_PreserveEffectiveToolRegistration(bool os27)
	{
		foreach (var mode in new ChatToolMode?[] { null, ChatToolMode.Auto, ChatToolMode.None, ChatToolMode.RequireAny })
		{
			if (!os27 && mode == ChatToolMode.RequireAny)
				continue;
			var native = new AppleIntelligenceChatClient().ToNative(new ChatOptions
			{
				ToolMode = mode,
				Tools = [AIFunctionFactory.Create(() => "result", "sample")]
			}, default, os27)!;

			Assert.Equal(mode == ChatToolMode.None ? ToolCallingModeNative.Disallowed :
				mode == ChatToolMode.RequireAny ? ToolCallingModeNative.Required : ToolCallingModeNative.Allowed,
				native.ToolCallingMode);
			Assert.Equal(mode == ChatToolMode.None ? 0 : 1, native.Tools?.Length ?? 0);
		}
	}

	public static TheoryData<string, ChatModelAvailability, string?> ModelAvailabilityCases => new()
	{
		{
			AppleIntelligenceModelAvailabilityNative.Available,
			ChatModelAvailability.Available,
			null
		},
		{
			AppleIntelligenceModelAvailabilityNative.DeviceNotEligible,
			ChatModelAvailability.NotSupported,
			AppleIntelligenceModelAvailabilityNative.DeviceNotEligible
		},
		{
			AppleIntelligenceModelAvailabilityNative.AppleIntelligenceNotEnabled,
			ChatModelAvailability.Disabled,
			AppleIntelligenceModelAvailabilityNative.AppleIntelligenceNotEnabled
		},
		{
			AppleIntelligenceModelAvailabilityNative.ModelNotReady,
			ChatModelAvailability.NotReady,
			AppleIntelligenceModelAvailabilityNative.ModelNotReady
		},
		{
			"futureNativeReason",
			ChatModelAvailability.Unknown,
			"futureNativeReason"
		},
	};

	// Apple's native unavailable reasons are preserved independently of its effective MEAI capabilities.
	[Theory]
	[MemberData(nameof(ModelAvailabilityCases))]
	public void ModelStatus_MapsAndPreservesNativeReason(
		string providerCode,
		ChatModelAvailability expectedAvailability,
		string? expectedReason)
	{
		var status = AppleIntelligenceChatClient.CreateModelStatus(providerCode, null);
		Assert.Equal(expectedAvailability, status.Availability);
		Assert.Equal(expectedReason, status.UnavailableReason);
		Assert.NotNull(status.Capabilities);
		Assert.Contains(ChatModelCapability.Text, status.Capabilities);
		Assert.Contains(ChatModelCapability.Streaming, status.Capabilities);
		Assert.Contains(ChatModelCapability.StructuredOutput, status.Capabilities);
		Assert.Contains(ChatModelCapability.ToolCalling, status.Capabilities);
	}

	// Apple's adapter exposes OS26 tools/structured output, adds OS27 token usage/vision, but does not map ReasoningOptions.
	[Theory]
	[InlineData(null)]
	[InlineData((long)AppleIntelligenceModelCapabilitiesNative.None)]
	[InlineData((long)AppleIntelligenceModelCapabilitiesNative.Vision)]
	[InlineData((long)AppleIntelligenceModelCapabilitiesNative.GuidedGeneration)]
	[InlineData((long)AppleIntelligenceModelCapabilitiesNative.Reasoning)]
	[InlineData((long)AppleIntelligenceModelCapabilitiesNative.ToolCalling)]
	[InlineData((long)(
		AppleIntelligenceModelCapabilitiesNative.Vision |
		AppleIntelligenceModelCapabilitiesNative.GuidedGeneration |
		AppleIntelligenceModelCapabilitiesNative.Reasoning |
		AppleIntelligenceModelCapabilitiesNative.ToolCalling))]
	public void ModelStatus_AdvertisesOnlyEffectiveCapabilities(long? nativeFlags)
	{
		var nativeCapabilities = nativeFlags is { } rawFlags
			? (AppleIntelligenceModelCapabilitiesNative?)(ulong)rawFlags
			: null;
		var status = AppleIntelligenceChatClient.CreateModelStatus(
			AppleIntelligenceModelAvailabilityNative.Available, nativeCapabilities);
		var capabilities = Assert.IsAssignableFrom<IReadOnlyCollection<ChatModelCapability>>(status.Capabilities);
		Assert.Contains(ChatModelCapability.Text, capabilities);
		Assert.Contains(ChatModelCapability.Streaming, capabilities);
		Assert.Contains(ChatModelCapability.StructuredOutput, capabilities);
		Assert.Contains(ChatModelCapability.ToolCalling, capabilities);
		Assert.Equal(nativeCapabilities.HasValue, capabilities.Contains(ChatModelCapability.TokenUsage));
		Assert.Equal(
			nativeCapabilities is { } flags &&
			(flags & AppleIntelligenceModelCapabilitiesNative.Vision) != 0,
			capabilities.Contains(ChatModelCapability.ImageInput));
		Assert.DoesNotContain(ChatModelCapability.Reasoning, capabilities);
		Assert.Equal(
			4 +
			(nativeCapabilities.HasValue ? 1 : 0) +
			(nativeCapabilities is { } imageFlags &&
				(imageFlags & AppleIntelligenceModelCapabilitiesNative.Vision) != 0 ? 1 : 0),
			capabilities.Count);
	}

	// Each concrete helper read traverses the native bridge; no cached readiness object is reused.
	[Fact]
	public async Task GetModelStatusAsync_ReadsCurrentNativeSnapshot()
	{
		var first = await AppleIntelligenceChatClient.GetModelStatusAsync();
		var second = await AppleIntelligenceChatClient.GetModelStatusAsync();
		Assert.NotSame(first, second);
		var availability = ChatClientNative.GetModelAvailability();
		var expected = AppleIntelligenceChatClient.CreateModelStatus(
			availability,
			ChatClientNative.GetModelCapabilities() is { } capabilities
				? (AppleIntelligenceModelCapabilitiesNative?)capabilities.UInt64Value
				: null);
		Assert.Equal(expected.Availability, second.Availability);
		Assert.Equal(expected.UnavailableReason, second.UnavailableReason);
		Assert.Equal(expected.Capabilities, second.Capabilities);
	}

	[Fact]
	public async Task GetModelStatusAsync_PreCancelled_Throws()
	{
		using var cts = new CancellationTokenSource();
		cts.Cancel();
		await Assert.ThrowsAsync<OperationCanceledException>(
			() => AppleIntelligenceChatClient.GetModelStatusAsync(cts.Token));
	}

	// Both Apple completion callbacks must reject missing/empty native payloads, even after streaming progress.
	[Fact]
	public void NativeSuccess_RejectsNullEmptyAndMalformedResponse()
	{
		Assert.Throws<InvalidOperationException>(() => AppleIntelligenceChatClient.FromNativeChatResponse(null));
		using var empty = new ChatResponseNative([]);
		Assert.Throws<InvalidOperationException>(() => AppleIntelligenceChatClient.ValidateNativeResponse(empty));
		using var emptyText = new ChatResponseNative([new ChatMessageNative
		{
			Role = ChatRoleNative.Assistant, Contents = [new TextContentNative("")]
		}]);
		Assert.Throws<InvalidOperationException>(() => AppleIntelligenceChatClient.ValidateNativeResponse(emptyText));
		using var valid = new ChatResponseNative([new ChatMessageNative
		{
			Role = ChatRoleNative.Assistant, Contents = [new TextContentNative("Hello")]
		}]);
		Assert.Equal("Hello", AppleIntelligenceChatClient.FromNativeChatResponse(valid).Text);
	}

	// Apple image support is OS-gated even if the content otherwise converts to a native image.
	[Fact]
	public void ValidateImageInput_BeforeOS27_RejectsInsteadOfOmitting()
	{
		Assert.Throws<NotSupportedException>(() => AppleIntelligenceChatClient.ValidateImageInput(false));
		AppleIntelligenceChatClient.ValidateImageInput(true);
	}

	// Foundation Models tool transcript entries require named FunctionResultContent, not standalone image attachments.
	[Fact]
	public void ToNative_ToolImageWithoutCallIdentity_RejectsInsteadOfOmitting()
	{
		Assert.Throws<NotSupportedException>(() => AppleIntelligenceChatClient.ToNative(
			[new(ChatRole.Tool, [new DataContent(new byte[] { 0 }, "image/png")]),
				new(ChatRole.User, "Hello")], null));
	}

	private sealed class UnrecognizedContent : AIContent;
}
#endif
