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

	// Apple's inline transcript must omit custom items individually, without replacing or reordering supported chunks.
	[Theory]
	[InlineData("system")]
	[InlineData("user")]
	[InlineData("assistant")]
	public void ToNative_MixedUnknownAndText_PreservesSupportedOrder(string role)
	{
		var native = Assert.Single(AppleIntelligenceChatClient.ToNative(
			[new(new ChatRole(role), [new UnrecognizedContent(), new TextContent("first"),
				new UnrecognizedContent(), new TextContent("second"), new UnrecognizedContent()])], null));

		Assert.Collection(native.Contents,
			content => Assert.Equal("first", Assert.IsType<TextContentNative>(content).Text),
			content => Assert.Equal("second", Assert.IsType<TextContentNative>(content).Text));
	}

	// Apple has no blank Tool transcript entry; custom-only Tool turns retain the existing empty-tool omission policy.
	[Fact]
	public void ToNative_UnknownOnlyTool_OmitsEmptyToolTurn()
	{
		var native = Assert.Single(AppleIntelligenceChatClient.ToNative(
			[new(ChatRole.Tool, [new UnrecognizedContent()]), new(ChatRole.User, "Hello")], null));

		Assert.Equal(ChatRoleNative.User, native.Role);
	}

	// Foundation Models has no standalone Tool text/image transcript entry; function role rules live in the shared base.
	[Theory]
	[InlineData("tool", "text")]
	[InlineData("tool", "data")]
	[InlineData("tool", "uri")]
	public async Task ToolRole_WithNonResultContent_RejectsBeforeInference(string role, string kind)
	{
		AIContent content = kind switch
		{
			"text" => new TextContent("text"),
			"data" => new DataContent(new byte[] { 0 }, "image/png"),
			_ => new UriContent("file:///image.png", "image/png")
		};
		using var client = new AppleIntelligenceChatClient();
		ChatMessage[] messages = [new(new ChatRole(role), [new UnrecognizedContent(), content]),
			new(ChatRole.User, "Hello")];

		foreach (var streaming in new[] { false, true })
		{
			var error = await Assert.ThrowsAsync<NotSupportedException>(async () =>
			{
				if (streaming)
					await client.GetStreamingResponseAsync(messages).ToChatResponseAsync();
				else
					await client.GetResponseAsync(messages);
			});
			Assert.Contains(content.GetType().Name, error.Message, StringComparison.Ordinal);
			Assert.Contains(role, error.Message, StringComparison.Ordinal);
		}
	}

	// Unknown roles are discarded before Apple's content validation, even when their standard content would be unsupported.
	[Fact]
	public void ToNative_UnknownRoleWithUnsupportedContent_RemainsIgnored()
	{
		var native = Assert.Single(AppleIntelligenceChatClient.ToNative(
			[new(new ChatRole("custom"), [new HostedFileContent("file-1")]), new(ChatRole.User, "Hello")], null));

		Assert.Equal(ChatRoleNative.User, native.Role);
		Assert.Equal("Hello", Assert.IsType<TextContentNative>(Assert.Single(native.Contents)).Text);
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

	// Foundation Models has no schema-free JSON generation mode.
	[Fact]
	public void ToNative_SchemaFreeJson_RejectsBeforeGeneration()
	{
		var error = Assert.Throws<NotSupportedException>(() => new AppleIntelligenceChatClient().ToNative(new ChatOptions
		{
			ResponseFormat = ChatResponseFormat.Json
		}, default));

		Assert.Contains("explicit JSON schema", error.Message, StringComparison.Ordinal);
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

	// MEAI modes have public constructors; equivalent modes need not be the shared singleton.
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void ToNative_IndependentOptionalModes_UseTypeSemantics(bool os27)
	{
		ChatToolMode[] modes = [new AutoChatToolMode(), new NoneChatToolMode()];
		foreach (var mode in modes)
		{
			Assert.NotSame(mode is AutoChatToolMode ? ChatToolMode.Auto : ChatToolMode.None, mode);
			var options = new ChatOptions
			{
				ToolMode = mode,
				Tools = [AIFunctionFactory.Create(() => "result", "sample")]
			};

			var native = new AppleIntelligenceChatClient().ToNative(options, default, os27)!;

			Assert.Equal(mode is NoneChatToolMode ? ToolCallingModeNative.Disallowed : ToolCallingModeNative.Allowed,
				native.ToolCallingMode);
			Assert.Equal(mode is NoneChatToolMode ? 0 : 1, native.Tools?.Length ?? 0);
		}
	}

	// A non-singleton None must omit unsupported tool definitions before Apple's callback validation.
	[Fact]
	public void ToNative_IndependentNoneMode_DoesNotValidateIgnoredTools()
	{
		var native = new AppleIntelligenceChatClient().ToNative(new ChatOptions
		{
			ToolMode = new NoneChatToolMode(),
			Tools = [new UnsupportedTool()]
		}, default, supportsOS27: false)!;

		Assert.Null(native.Tools);
		Assert.Equal(ToolCallingModeNative.Disallowed, native.ToolCallingMode);
	}

	private sealed class UnsupportedTool : AITool;

#pragma warning disable MEAI001 // Exercise the pinned MEAI background option's native mapping.
	// Apple's default native session flags must remain foreground for both false and unspecified background options.
	[Theory]
	[InlineData(null)]
	[InlineData(false)]
	public void ToNative_DefaultConversationOptions_PreserveForegroundBehavior(bool? background)
	{
		using var client = new AppleIntelligenceChatClient();
		Assert.Null(client.ToNative(null, default));
		var native = client.ToNative(new ChatOptions
		{
			AllowBackgroundResponses = background
		}, default);

		Assert.NotNull(native);
		Assert.Null(native.Tools);
	}

#pragma warning restore MEAI001

	// Apple's final transcript message becomes the current Prompt; instructions and prior responses cannot fill that role.
	[Theory]
	[InlineData("assistant", false)]
	[InlineData("assistant", true)]
	[InlineData("trailing-system", false)]
	[InlineData("trailing-system", true)]
	[InlineData("trailing-assistant", false)]
	[InlineData("trailing-assistant", true)]
	[InlineData("trailing-tool", false)]
	[InlineData("trailing-tool", true)]
	public async Task CurrentPrompt_WithoutFinalUser_RejectsBeforeInference(string scenario, bool streaming)
	{
		ChatMessage[] messages = scenario switch
		{
			"assistant" => [new(ChatRole.Assistant, "Prior response")],
			"trailing-system" => [new(ChatRole.User, "Hello"), new(ChatRole.System, "Instructions")],
			"trailing-assistant" => [new(ChatRole.User, "Hello"), new(ChatRole.Assistant, "Prior response")],
			_ => [
				new(ChatRole.User, "Hello"),
				new(ChatRole.Assistant, [new FunctionCallContent("call-1", "sample")]),
				new(ChatRole.Tool, [new FunctionResultContent("call-1", "result")])
			],
		};
		using var client = new AppleIntelligenceChatClient();
		var options = new ChatOptions { Instructions = "Follow these instructions." };

		var error = await Assert.ThrowsAsync<ArgumentException>(async () =>
		{
			if (streaming)
				await client.GetStreamingResponseAsync(messages, options).ToChatResponseAsync();
			else
				await client.GetResponseAsync(messages, options);
		});

		Assert.Equal("messages", error.ParamName);
		Assert.Contains("last User message", error.Message, StringComparison.Ordinal);
	}

	// Explicit blank User turns remain actual prompts, not missing content to be replaced by synthetic text.
	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData(" \r\n\t")]
	public void ToNative_ExplicitBlankUser_PreservesCurrentTurn(string? text)
	{
		var native = AppleIntelligenceChatClient.ToNative(
			[new(ChatRole.User, [new TextContent(text)])],
			new ChatOptions { Instructions = "Instructions" });

		Assert.Equal(2, native.Length);
		Assert.Equal(ChatRoleNative.User, native[^1].Role);
		Assert.Equal(text ?? "", Assert.IsType<TextContentNative>(Assert.Single(native[^1].Contents)).Text);
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

	// OS26 retains known tools/structured support; OS27 discovery gates each conditional capability.
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
		var structured = nativeCapabilities is null ||
			(nativeCapabilities.Value & AppleIntelligenceModelCapabilitiesNative.GuidedGeneration) != 0;
		var tools = nativeCapabilities is null ||
			(nativeCapabilities.Value & AppleIntelligenceModelCapabilitiesNative.ToolCalling) != 0;
		Assert.Equal(structured, capabilities.Contains(ChatModelCapability.StructuredOutput));
		Assert.Equal(tools, capabilities.Contains(ChatModelCapability.ToolCalling));
		Assert.Equal(nativeCapabilities.HasValue, capabilities.Contains(ChatModelCapability.TokenUsage));
		Assert.Equal(
			nativeCapabilities is { } flags &&
			(flags & AppleIntelligenceModelCapabilitiesNative.Vision) != 0,
			capabilities.Contains(ChatModelCapability.ImageInput));
		Assert.DoesNotContain(ChatModelCapability.Reasoning, capabilities);
		Assert.Equal(
			2 + (structured ? 1 : 0) + (tools ? 1 : 0) +
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

	// Apple's status helper probes native model availability independently of chat generation; cancellation must precede that probe.
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
