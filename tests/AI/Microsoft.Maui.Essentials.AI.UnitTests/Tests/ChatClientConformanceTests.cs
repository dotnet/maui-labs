using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Maui.Essentials.AI.DeviceTests;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Maui.Essentials.AI.UnitTests;

// These scripted controls test the shared harness, not any native or cloud implementation.
public class ChatClientConformanceTests(ITestOutputHelper output)
{
	[Theory]
	[InlineData(false, false)]
	[InlineData(false, true)]
	[InlineData(true, false)]
	[InlineData(true, true)]
	public async Task RequiredTools_ExerciseInvocationAndRejection(bool streaming, bool named)
	{
		await new ToolBinding().RequiredToolCallbacks_FinishOnceAndPreserveContext(streaming, named);
		var rejected = new RejectedToolBinding();
		await rejected.RequiredToolMode_WhenUnsupported_RejectsBeforeInference(streaming, named, "simple");
		if (named)
		{
			await rejected.RequiredToolMode_WhenUnsupported_RejectsBeforeInference(streaming, named, "formatted");
			await rejected.RequiredToolMode_WhenUnsupported_RejectsBeforeInference(streaming, named, "nullable");
		}
		// Inference methods must return without touching a rejection-only provider.
		await rejected.RequiredToolCallbacks_FinishOnceAndPreserveContext(streaming, named);
		await rejected.RequiredTool_WithFormattedParametersAndDictionaryReturn_InvokesExactlyOnce(streaming);
		await rejected.RequiredTool_WithNullableParameters_InvokesExactlyOnce(streaming);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Tools_ExerciseFormattedNullableCancellationAndActivity(bool streaming)
	{
		var binding = new ToolBinding();
		await binding.RequiredTool_WithFormattedParametersAndDictionaryReturn_InvokesExactlyOnce(streaming);
		await binding.RequiredTool_WithNullableParameters_InvokesExactlyOnce(streaming);
		await binding.ToolInvocation_CallerCancellation_PreservesCancelledTask(streaming);
		await binding.ToolCallbacks_InheritChatTraceAndSpanAcrossAwaits(streaming);
	}

	[Fact]
	public void ToolStreamValidation_AcceptsWhitespaceButRejectsNullArtifacts()
	{
		var binding = new ToolBinding();
		binding.AssertToolStream(
		[
			("Text", "\n  "),
			(nameof(TextReasoningContent), ""),
			("ToolCall", "SearchLandmarks(call-1)"),
			("Text", "Here are the landmarks.")
		]);

		Assert.ThrowsAny<Exception>(() => binding.AssertToolStream(
		[
			("Text", ""),
			("ToolCall", "SearchLandmarks(call-1)"),
			("Text", "Here are the landmarks.")
		]));
		Assert.ThrowsAny<Exception>(() => binding.AssertToolStream(
		[
			("Text", " null "),
			("ToolCall", "SearchLandmarks(call-1)"),
			("Text", "Here are the landmarks.")
		]));
		Assert.ThrowsAny<Exception>(() => binding.AssertToolStream(
		[
			("Text", "<null>"),
			("ToolCall", "SearchLandmarks(call-1)"),
			("Text", "Here are the landmarks.")
		]));
	}

	[Fact]
	public void Metadata_ExercisesSharedHooks()
	{
		var binding = new MetadataBinding();
		binding.GetService_WithNullServiceType_ThrowsArgumentNullException();
		binding.GetService_ReturnsMetadataWithCorrectProviderAndModel();
		binding.GetService_ReturnsNullForUnknownService();
		binding.GetService_ReturnsNullForKeyedServices();
		binding.GetService_ReturnsItselfForMatchingType();
		binding.GetService_ReturnsItselfForIChatClientType();
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Messages_ExercisePromptMediaAndContentPolicies(bool streaming)
	{
		var binding = new MessageBinding();
		await binding.InstructionsOnly_WhenPromptRequired_Rejects("system", streaming);
		await binding.InstructionsOnly_WhenPromptRequired_Rejects("instructions", streaming);
		await binding.GetResponseAsync_WithUnknownContentAlongsideText_OmitsOnlyUnknownItems(streaming);
		await binding.GetResponseAsync_WithOnlyUnknownContent_PreservesFollowingUserTurn(streaming);
		await binding.GetResponseAsync_WithOnlyNullTextContent_PreservesFollowingUserTurn();
		await binding.GetResponseAsync_WithBlankConversationTurns_PreservesFollowingUserTurn(false, streaming);
		await binding.GetResponseAsync_WithBlankConversationTurns_PreservesFollowingUserTurn(true, streaming);
		await binding.GetResponseAsync_WithUnsupportedRoleAmongValidMessages_IgnoresUnsupportedRole();
		await binding.GetResponseAsync_WithFunctionHistory_PreservesUserText(streaming);
		await binding.GetResponseAsync_WithFunctionHistory_RetainsToolResultContext(streaming);
		foreach (var media in new[] { "application/pdf", "audio/ogg", "text/plain", "application/octet-stream" })
		{
			await binding.UnsupportedMedia_RejectsInsteadOfOmitting(media, streaming);
			await binding.GetResponseAsync_WithKnownContentAndUnsupportedMedia_AppliesProviderPolicy(media, streaming);
		}
		foreach (var media in new[] { "image/png", "application/pdf" })
		{
			await binding.UnsupportedBinaryContent_RejectsBeforeInference(media, streaming);
			await binding.GetResponseAsync_WithBinaryContentAndText_AppliesProviderPolicy(media, streaming);
		}
		foreach (var kind in new[] { "hosted-file", "hosted-vector-store", "reasoning", "usage", "error",
			"code-call", "code-result", "image-call", "image-result", "web-call", "web-result",
			"mcp-call", "mcp-result", "approval-request", "approval-response" })
			await binding.UnsupportedStandardContent_RejectsInEveryRoleBeforeInference(kind);
		foreach (var role in new[] { "system", "user", "tool" })
			await binding.MisplacedFunctionContent_RejectsBeforeInference(role, true);
		foreach (var role in new[] { "system", "user", "assistant" })
			await binding.MisplacedFunctionContent_RejectsBeforeInference(role, false);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Media_ExerciseOmissionAndSupportedBinaryContent(bool streaming)
	{
		var omitted = new OmittedMediaBinding();
		foreach (var media in new[] { "application/pdf", "audio/ogg", "text/plain", "application/octet-stream" })
			await omitted.GetResponseAsync_WithKnownContentAndUnsupportedMedia_AppliesProviderPolicy(media, streaming);
		var supported = new BinaryMediaBinding();
		foreach (var media in new[] { "image/png", "application/pdf" })
			await supported.GetResponseAsync_WithBinaryContentAndText_AppliesProviderPolicy(media, streaming);
		foreach (var media in new[] { "application/pdf", "audio/ogg", "text/plain", "application/octet-stream" })
			await supported.UnsupportedMedia_RejectsInsteadOfOmitting(media, streaming);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Options_ExerciseStatefulAndExplicitSchemaPolicies(bool streaming)
	{
		var binding = new OptionsBinding();
		foreach (var name in new[] { "ContinuationToken", "ConversationId", "AllowBackgroundResponses" })
			await binding.UnsupportedConversationOptions_RejectBeforeInference(name, streaming);
		await binding.SchemaFreeJson_WhenExplicitSchemaRequired_RejectsBeforeInference(streaming);
		if (streaming)
			await binding.GetStreamingResponseAsync_WithResponseFormat_AcceptsJsonFormat();
		else
			await binding.GetResponseAsync_WithResponseFormat_AcceptsJsonFormat();
		await binding.GetResponseAsync_WithChatOptions_AcceptsValidOptions();
		await binding.GetStreamingResponseAsync_WithChatOptions_AcceptsValidOptions();
		await binding.GetResponseAsync_WithExtremeTemperature_HandlesGracefully();
	}

	[Theory]
	[InlineData(false, false)]
	[InlineData(false, true)]
	[InlineData(true, false)]
	[InlineData(true, true)]
	public async Task Instructions_ExerciseComplementarySourcesAndPlacement(bool options, bool middle)
	{
		await new OptionsBinding().GetResponseAsync_WithComplementarySystemInstructions_PreservesBoth(options, middle);
	}

	[Fact]
	public async Task Schema_UnsupportedMeansRejectionNotSuccessfulNoOp()
	{
		var binding = new SchemaBinding();
		foreach (var scenario in new[] { "weather", "simple", "itinerary", "custom-options" })
		{
			await binding.StructuredOutput_WhenUnsupported_RejectsBeforeInference(scenario, false);
			await binding.StructuredOutput_WhenUnsupported_RejectsBeforeInference(scenario, true);
		}
		foreach (var scenario in new[] { "typed-weather", "typed-simple", "typed-itinerary", "typed-options" })
			await binding.StructuredOutput_WhenUnsupported_RejectsBeforeInference(scenario, false);
		await binding.SchemaFreeJson_WhenUnsupported_RejectsBeforeInference(false);
		await binding.SchemaFreeJson_WhenUnsupported_RejectsBeforeInference(true);
		await binding.TypedPromptFallback_WhenUnsupported_RejectsBeforeInference();
		await VerifyRejectedSchemaInferenceReturnsAsync(binding);

		var explicitSchema = new ExplicitSchemaBinding();
		await explicitSchema.SchemaFreeJson_WhenUnsupported_RejectsBeforeInference(false);
		await explicitSchema.SchemaFreeJson_WhenUnsupported_RejectsBeforeInference(true);
		await explicitSchema.TypedPromptFallback_WhenUnsupported_RejectsBeforeInference();
		await explicitSchema.GetResponseAsync_WithJsonFormatWithoutSchema_DoesNotThrow();
		await explicitSchema.GetStreamingResponseAsync_WithJsonFormatWithoutSchema_DoesNotThrow();
		await explicitSchema.GetResponseAsync_WithTypedPromptFallback_UsesSchemaFreeJson();
	}

	private static async Task VerifyRejectedSchemaInferenceReturnsAsync(SchemaBinding binding)
	{
		await binding.GetResponseAsync_WithJsonSchemaFormat_ReturnsStructuredResponse();
		await binding.GetResponseAsync_WithJsonSchemaFormat_ReturnsValidJson();
		await binding.GetStreamingResponseAsync_WithJsonSchemaFormat_StreamsValidJson();
		await binding.GetResponseAsync_WithComplexJsonSchema_ReturnsStructuredResponse();
		await binding.GetResponseAsync_WithJsonSchemaFormatAndCustomOptions_Works();
		await binding.GetResponseAsyncT_ReturnsStructuredResponse();
		await binding.GetResponseAsyncT_WithSimpleType_ReturnsDeserializedResult();
		await binding.GetResponseAsyncT_WithComplexType_ReturnsStructuredResponse();
		await binding.GetResponseAsyncT_WithChatOptions_ReturnsStructuredResponse();
		await binding.GetResponseAsyncT_WithMessageList_ReturnsStructuredResponse();
		await binding.GetResponseAsync_WithJsonFormatWithoutSchema_DoesNotThrow();
		await binding.GetStreamingResponseAsync_WithJsonFormatWithoutSchema_DoesNotThrow();
		await binding.GetResponseAsync_WithTypedPromptFallback_UsesSchemaFreeJson();
	}

	[Fact]
	public async Task Schema_ExercisesExactStructuredResultsAndSchemaFreeSupport()
	{
		var binding = new SupportedSchemaBinding();
		await binding.GetResponseAsync_WithJsonSchemaFormat_ReturnsStructuredResponse();
		await binding.GetResponseAsync_WithJsonSchemaFormat_ReturnsValidJson();
		await binding.GetStreamingResponseAsync_WithJsonSchemaFormat_StreamsValidJson();
		await binding.GetResponseAsync_WithComplexJsonSchema_ReturnsStructuredResponse();
		await binding.GetResponseAsync_WithJsonSchemaFormatAndCustomOptions_Works();
		await binding.GetResponseAsyncT_ReturnsStructuredResponse();
		await binding.GetResponseAsyncT_WithSimpleType_ReturnsDeserializedResult();
		await binding.GetResponseAsyncT_WithComplexType_ReturnsStructuredResponse();
		await binding.GetResponseAsyncT_WithChatOptions_ReturnsStructuredResponse();
		await binding.GetResponseAsyncT_WithMessageList_ReturnsStructuredResponse();
		await binding.GetResponseAsync_WithJsonFormatWithoutSchema_DoesNotThrow();
		await binding.GetStreamingResponseAsync_WithJsonFormatWithoutSchema_DoesNotThrow();
		await binding.GetResponseAsync_WithTypedPromptFallback_UsesSchemaFreeJson();
	}

	[Fact]
	public void TripItinerarySchema_RequiresExactlyTwoActivities()
	{
		var format = Assert.IsType<ChatResponseFormatJson>(
			ChatResponseFormat.ForJsonSchema<ChatClientJsonSchemaTestsBase<ControlClient>.TripItinerary>(JsonSerializerOptions.Web));
		var activities = format.Schema!.Value.GetProperty("properties").GetProperty("activities");

		Assert.Equal(2, activities.GetProperty("minItems").GetInt32());
		Assert.Equal(2, activities.GetProperty("maxItems").GetInt32());
	}

	[Fact]
	public void RejectionContracts_AreDiscoverableWithoutRequiresModel()
	{
		AssertModelFree(typeof(ChatClientFunctionCallingTestsBase<ControlClient>),
			nameof(RejectedToolBinding.RequiredToolMode_WhenUnsupported_RejectsBeforeInference));
		foreach (var name in new[]
		{
			nameof(SchemaBinding.StructuredOutput_WhenUnsupported_RejectsBeforeInference),
			nameof(SchemaBinding.SchemaFreeJson_WhenUnsupported_RejectsBeforeInference),
			nameof(SchemaBinding.TypedPromptFallback_WhenUnsupported_RejectsBeforeInference)
		})
			AssertModelFree(typeof(ChatClientJsonSchemaTestsBase<ControlClient>), name);
		AssertModelFree(typeof(ChatClientOptionsTestsBase<ControlClient>),
			nameof(OptionsBinding.SchemaFreeJson_WhenExplicitSchemaRequired_RejectsBeforeInference));
		AssertModelFree(typeof(ChatClientMessagesTestsBase<ControlClient>),
			nameof(MessageBinding.UnsupportedBinaryContent_RejectsBeforeInference));
	}

	private static void AssertModelFree(Type suite, string methodName)
	{
		var method = suite.GetMethod(methodName);
		Assert.NotNull(method);
		Assert.Contains(method.GetCustomAttributesData(), attribute =>
			typeof(FactAttribute).IsAssignableFrom(attribute.AttributeType));
		Assert.DoesNotContain(method.GetCustomAttributesData(), attribute =>
			attribute.AttributeType == typeof(TraitAttribute) &&
			Equals(attribute.ConstructorArguments[0].Value, TestTraits.RequiresModel));
	}

	[Fact]
	public async Task Usage_InputOnlyCountsDriveTelemetry()
	{
		var binding = new UsageBinding(output);
		await binding.GetResponseAsync_ReturnsExpectedUsage();
		await binding.GetStreamingResponseAsync_ReturnsExpectedUsage();
	}

	private sealed class ToolBinding : ChatClientFunctionCallingTestsBase<ToolControlClient>
	{
		protected override bool? SupportsRequiredToolMode => true;
		protected override bool SupportsDeterministicChainedToolCalls => true;
		protected override bool SupportsBaselineSamplingOptions => true;
		protected override bool PreservesToolActivityContext => true;
		protected override bool PreservesToolCancellation => true;
		protected override IChatClient EnableFunctionCalling(ToolControlClient client, Action<ChatOptions>? configureOptions = null)
		{
			var builder = client.AsBuilder().UseFunctionInvocation();
			if (configureOptions is not null)
				builder.ConfigureOptions(configureOptions);
			return builder.Build();
		}

		public void AssertToolStream(IReadOnlyCollection<(string Type, string Value)> rawLog) =>
			AssertToolCallStreamContent(rawLog);
	}

	private sealed class RejectedToolBinding : ChatClientFunctionCallingTestsBase<ControlClient>
	{
		protected override bool? SupportsRequiredToolMode => false;
	}

	private sealed class MetadataBinding : ChatClientGetServiceTestsBase<ControlClient>
	{
		protected override string ExpectedProviderName => "control";
		protected override string ExpectedDefaultModelId => "control-model";
		protected override void AssertProviderMetadata(ChatClientMetadata metadata) =>
			Assert.Null(metadata.ProviderUri);
	}

	private sealed class MessageBinding : ChatClientMessagesTestsBase<ControlClient>
	{
		protected override bool RequiresPrompt => true;
		protected override bool RejectsUnsupportedMedia => true;
		protected override bool RejectsUnsupportedStandardContent(string kind, ChatRole role) => true;
		protected override bool RejectsMisplacedFunctionContent(ChatRole role, bool isCall) => true;
	}

	private sealed class OptionsBinding : ChatClientOptionsTestsBase<ControlClient>
	{
		protected override bool RejectsStatefulConversationOptions => true;
		protected override bool RequiresExplicitJsonSchema => true;
		protected override bool SupportsReasoningEffortNone => true;
	}

	private sealed class SchemaBinding : ChatClientJsonSchemaTestsBase<ControlClient>
	{
		protected override bool SupportsStructuredOutput => false;
	}

	private sealed class ExplicitSchemaBinding : ChatClientJsonSchemaTestsBase<ControlClient>
	{
		protected override bool? RequiresExplicitJsonSchema => true;
	}

	private sealed class SupportedSchemaBinding : ChatClientJsonSchemaTestsBase<StructuredControlClient>
	{
		protected override bool? RequiresExplicitJsonSchema => false;
		protected override bool SupportsBaselineSamplingOptions => true;
	}

	private sealed class OmittedMediaBinding : ChatClientMessagesTestsBase<PermissiveMediaControlClient>;

	private sealed class BinaryMediaBinding : ChatClientMessagesTestsBase<BinaryMediaControlClient>
	{
		protected override bool RejectsUnsupportedMedia => true;
		protected override bool SupportsBinaryMedia(string mediaType) => mediaType is "image/png" or "application/pdf";
	}

	private sealed class UsageBinding(ITestOutputHelper output) : ChatClientUsageTestsBase<ControlClient>(output)
	{
		protected override bool IsOutputUsageAvailable => false;
	}

	private class ControlClient : IChatClient
	{
		protected virtual bool AcceptsMedia(string mediaType, bool useUri) => false;

		public object? GetService(Type serviceType, object? serviceKey = null)
		{
			ArgumentNullException.ThrowIfNull(serviceType);
			if (serviceKey is not null)
				return null;
			if (serviceType == typeof(ChatClientMetadata))
				return new ChatClientMetadata("control", defaultModelId: "control-model");
			return serviceType.IsInstanceOfType(this) ? this : null;
		}

		public virtual Task<ChatResponse> GetResponseAsync(
			IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(messages);
			cancellationToken.ThrowIfCancellationRequested();
#pragma warning disable MEAI001
			if (options?.ContinuationToken is not null)
				throw new NotSupportedException(nameof(ChatOptions.ContinuationToken));
			if (options?.ConversationId is not null)
				throw new NotSupportedException(nameof(ChatOptions.ConversationId));
			if (options?.AllowBackgroundResponses is true)
				throw new NotSupportedException(nameof(ChatOptions.AllowBackgroundResponses));
#pragma warning restore MEAI001
			if (options?.ResponseFormat is not null)
				throw new NotSupportedException("An explicit JSON schema is required; structured output is unavailable.");
			if (options?.ToolMode is RequiredChatToolMode)
				throw new NotSupportedException("Required tools are unavailable.");
			AssertDeterministicSampling(options);
			var history = messages.Where(message => message.Role == ChatRole.System ||
				message.Role == ChatRole.User || message.Role == ChatRole.Assistant ||
				message.Role == ChatRole.Tool).ToArray();
			foreach (var message in history)
			{
				foreach (var content in message.Contents)
				{
					if (content is DataContent data)
					{
						if (!AcceptsMedia(data.MediaType, useUri: false))
							throw new NotSupportedException(data.MediaType);
						continue;
					}
					if (content is UriContent uri)
					{
						if (!AcceptsMedia(uri.MediaType, useUri: true))
							throw new NotSupportedException(uri.MediaType);
						continue;
					}
					if (content is FunctionCallContent && message.Role == ChatRole.Assistant ||
						content is FunctionResultContent && message.Role == ChatRole.Tool)
						continue;
					if (content is not TextContent && content.GetType().Assembly == typeof(AIContent).Assembly)
						throw new NotSupportedException(content.GetType().Name);
				}
			}
			if (!history.Any(message => message.Role == ChatRole.User || message.Role == ChatRole.Assistant))
				throw new ArgumentException("A prompt is required.", nameof(messages));
			var lastUserText = history.LastOrDefault(message => message.Role == ChatRole.User)?.Text ?? "";
			var text = "HELLO";
			if (lastUserText.Contains("secret word", StringComparison.Ordinal))
				text = string.Join(" ", history.Select(message => message.Text)).Contains("PAPAYA", StringComparison.Ordinal)
					? "PAPAYA" : "NONE";
			else if (lastUserText.Contains("city and temperature", StringComparison.Ordinal))
			{
				var result = Assert.IsType<Dictionary<string, object?>>(history
					.SelectMany(message => message.Contents).OfType<FunctionResultContent>().Single().Result);
				text = $"{result["city"]} {result["temperatureCelsius"]}";
			}
			else if (lastUserText.Contains("slot-one", StringComparison.Ordinal) &&
				lastUserText.Contains("slot-two", StringComparison.Ordinal))
			{
				var instructions = options?.Instructions + " " + string.Join(" ",
					history.Where(message => message.Role == ChatRole.System).Select(message => message.Text));
				text = (instructions.Contains("ALPHA", StringComparison.Ordinal) ? "ALPHA " : "") +
					(instructions.Contains("BETA", StringComparison.Ordinal) ? "BETA" : "");
			}
			return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text))
			{
				ModelId = "control-model",
				Usage = new() { InputTokenCount = 3 }
			});
		}

		public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
			IEnumerable<ChatMessage> messages, ChatOptions? options = null,
			[EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			await Task.Yield();
			var response = await GetResponseAsync(messages, options, cancellationToken);
			foreach (var message in response.Messages)
				yield return new ChatResponseUpdate(message.Role, message.Contents) { ModelId = response.ModelId };
			if (response.Usage is not null)
				yield return new ChatResponseUpdate(ChatRole.Assistant, [new UsageContent(response.Usage)]) { ModelId = response.ModelId };
		}

		public void Dispose() { }
	}

	private static void AssertDeterministicSampling(ChatOptions? options)
	{
		if (options?.Temperature is not null)
			Assert.Equal(ReasoningEffort.None, options.Reasoning?.Effort);
	}

	private sealed class PermissiveMediaControlClient : ControlClient
	{
		protected override bool AcceptsMedia(string mediaType, bool useUri) => true;
	}

	private sealed class BinaryMediaControlClient : ControlClient
	{
		protected override bool AcceptsMedia(string mediaType, bool useUri) =>
			!useUri && mediaType is "image/png" or "application/pdf";
	}

	private sealed class StructuredControlClient : ControlClient
	{
		public override Task<ChatResponse> GetResponseAsync(
			IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			AssertDeterministicSampling(options);
			var prompt = string.Join(" ", messages.Select(message => message.Text));
			object result;
			if (prompt.Contains("Space Needle", StringComparison.Ordinal))
			{
				result = new
				{
					Destination = "Seattle",
					Days = 1,
					Activities = new[]
					{
						new { Name = "Space Needle", Description = "View the city", Time = "09:00" },
						new { Name = "Pike Place Market", Description = "Explore the market", Time = "12:00" }
					}
				};
			}
			else if (prompt.Contains("Return location", StringComparison.Ordinal))
			{
				var city = prompt.Contains("Seattle", StringComparison.Ordinal) ? "Seattle" :
					prompt.Contains("Boston", StringComparison.Ordinal) ? "Boston" : "Chicago";
				result = new
				{
					Location = city,
					Temperature = city == "Seattle" ? 72 : city == "Boston" ? 68 : 65,
					Condition = city == "Chicago" ? "cloudy" : "sunny"
				};
			}
			else
				result = new { Message = "hello", greeting = "hello" };

			var jsonOptions = options?.ResponseFormat is ChatResponseFormatJson { Schema: { } schema } &&
				schema.GetProperty("properties").EnumerateObject().Any(property => char.IsLower(property.Name[0]))
					? JsonSerializerOptions.Web : JsonSerializerOptions.Default;
			return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant,
				JsonSerializer.Serialize(result, jsonOptions))));
		}
	}

	private sealed class ToolControlClient : ControlClient
	{
		public override Task<ChatResponse> GetResponseAsync(
			IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			AssertDeterministicSampling(options);
			var results = messages.SelectMany(message => message.Contents).OfType<FunctionResultContent>().ToArray();
			if (results.Length > 0)
				return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant,
					string.Join(" ", results.Select(result => result.Result?.ToString())))));

			var name = options!.Tools![0].Name;
			Dictionary<string, object?> arguments = name switch
			{
				"lookupRequiredValue" => new() { ["key"] = "secret" },
				"capture_formatted" => new()
				{
					["id"] = "8d418a4d-fd44-47f4-813d-08b6b40abeca",
					["timestamp"] = "2026-10-20T12:34:56+00:00"
				},
				"capture_nullable" => new() { ["value"] = null, ["label"] = "OPTIONAL_LABEL" },
				"GetWeather" => new() { ["location"] = "Seattle" },
				_ => []
			};
			return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant,
				[new FunctionCallContent("control-call", name, arguments)])));
		}
	}
}
